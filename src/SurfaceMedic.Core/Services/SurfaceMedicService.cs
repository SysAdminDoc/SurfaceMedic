using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SurfaceMedic.Core.Infrastructure;
using SurfaceMedic.Core.Models;

namespace SurfaceMedic.Core.Services;

public sealed partial class SurfaceMedicService : ISurfaceMedicService
{
    private const string BalancedOverlayGuid = "00000000-0000-0000-0000-000000000000";
    private const string EfficiencyOverlayGuid = "961cc777-2547-4f9d-8174-7d86181b8a7a";
    private const string BetterPerformanceOverlayGuid = "3af9b8d9-7c97-431d-ad78-34a8bfea439f";
    private const string PerformanceOverlayGuid = "ded574b5-45a0-4f42-8737-46345c09c238";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ProcessRunner _processRunner;
    private readonly PowerShellAdapter _powerShell;

    public SurfaceMedicService()
    {
        _processRunner = new ProcessRunner();
        _powerShell = new PowerShellAdapter(_processRunner);
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        const string operation = "Refresh dashboard";
        const string script = """
            $errors = @()
            $device = [ordered]@{ Manufacturer = $null; Model = $null; ProcessorName = $null; InstalledMemoryGb = $null }
            try {
                $cs = Get-CimInstance Win32_ComputerSystem
                $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
                $device.Manufacturer = [string]$cs.Manufacturer
                $device.Model = [string]$cs.Model
                if ($cpu) { $device.ProcessorName = ([string]$cpu.Name).Trim() }
                if ($cs.TotalPhysicalMemory) { $device.InstalledMemoryGb = [math]::Round(([double]$cs.TotalPhysicalMemory / 1GB), 1) }
            } catch { $errors += "Device inventory: $($_.Exception.Message)" }

            $osData = [ordered]@{ Caption = $null; DisplayVersion = $null; BuildNumber = $null; InstalledAt = $null; UptimeSeconds = $null }
            try {
                $os = Get-CimInstance Win32_OperatingSystem
                $version = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction SilentlyContinue
                $osData.Caption = [string]$os.Caption
                if ($version) { $osData.DisplayVersion = [string]$version.DisplayVersion }
                $osData.BuildNumber = [string]$os.BuildNumber
                if ($os.InstallDate) { $osData.InstalledAt = $os.InstallDate.ToString('o') }
                if ($os.LastBootUpTime) { $osData.UptimeSeconds = [math]::Max(0, ((Get-Date) - $os.LastBootUpTime).TotalSeconds) }
            } catch { $errors += "Operating system inventory: $($_.Exception.Message)" }

            $battery = [ordered]@{ IsPresent = $false; DesignCapacityMwh = $null; FullChargeCapacityMwh = $null; CycleCount = $null }
            $batteryPath = Join-Path $env:TEMP ("surfacemedic-battery-{0}.xml" -f $PID)
            try {
                Remove-Item -LiteralPath $batteryPath -Force -ErrorAction SilentlyContinue
                & powercfg /batteryreport /output $batteryPath /XML *> $null
                if (Test-Path -LiteralPath $batteryPath) {
                    $document = [xml](Get-Content -LiteralPath $batteryPath -Raw)
                    $batteryNode = $document.GetElementsByTagName('Battery') | Select-Object -First 1
                    if ($batteryNode -and $batteryNode.DesignCapacity) {
                        $battery.IsPresent = $true
                        $battery.DesignCapacityMwh = [double]$batteryNode.DesignCapacity
                        $battery.FullChargeCapacityMwh = [double]$batteryNode.FullChargeCapacity
                        if ($batteryNode.CycleCount -and ([string]$batteryNode.CycleCount) -match '^\d+$') {
                            $battery.CycleCount = [int]$batteryNode.CycleCount
                        }
                    }
                }
            } catch { $errors += "Battery inventory: $($_.Exception.Message)" }
            finally { Remove-Item -LiteralPath $batteryPath -Force -ErrorAction SilentlyContinue }

            $storage = [ordered]@{ Drive = 'C:'; TotalBytes = $null; FreeBytes = $null }
            try {
                $systemDrive = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
                if ($systemDrive) {
                    $storage.TotalBytes = [double]$systemDrive.Size
                    $storage.FreeBytes = [double]$systemDrive.FreeSpace
                }
            } catch { $errors += "Storage inventory: $($_.Exception.Message)" }

            $disks = @()
            try {
                foreach ($disk in @(Get-PhysicalDisk -ErrorAction Stop)) {
                    $wear = $null
                    $temperature = $null
                    try {
                        $reliability = $disk | Get-StorageReliabilityCounter -ErrorAction Stop
                        if ($null -ne $reliability.Wear) { $wear = [double]$reliability.Wear }
                        if ($null -ne $reliability.Temperature) { $temperature = [double]$reliability.Temperature }
                    } catch { }
                    $disks += [ordered]@{
                        Name = [string]$disk.FriendlyName
                        MediaType = [string]$disk.MediaType
                        ReportedHealth = [string]$disk.HealthStatus
                        WearPercent = $wear
                        TemperatureCelsius = $temperature
                    }
                }
            } catch { $errors += "Physical disk inventory: $($_.Exception.Message)" }

            $thermal = [ordered]@{ ThrottleEngagementCount = 0; FirmwareSpeedCapCount = 0; HardwareErrorCount = 0; ZoneTemperaturesCelsius = @() }
            $since = (Get-Date).AddDays(-7)
            try { $thermal.ThrottleEngagementCount = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Microsoft-Windows-Kernel-Power'; Id = 125; StartTime = $since } -ErrorAction Stop).Count } catch { }
            try { $thermal.FirmwareSpeedCapCount = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Microsoft-Windows-Kernel-Processor-Power'; Id = 37; StartTime = $since } -ErrorAction Stop).Count } catch { }
            try { $thermal.HardwareErrorCount = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Microsoft-Windows-WHEA-Logger'; StartTime = $since } -ErrorAction Stop).Count } catch { }
            try {
                $zones = Get-CimInstance -Namespace root/wmi -ClassName MSAcpi_ThermalZoneTemperature -ErrorAction Stop
                $thermal.ZoneTemperaturesCelsius = @($zones | ForEach-Object { [math]::Round((([double]$_.CurrentTemperature / 10) - 273.15), 1) } | Where-Object { $_ -gt 0 })
            } catch { }

            $result = [ordered]@{ Device = $device; OperatingSystem = $osData; Battery = $battery; Storage = $storage; Disks = @($disks); Thermal = $thermal; Errors = @($errors) }
            ConvertTo-Json -InputObject $result -Depth 6 -Compress
            """;

        var processResult = await _powerShell.RunAsync(
            script,
            operation,
            callbacks,
            streamOutput: false,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(processResult, operation);

        var payload = DeserializeJson<DashboardPayload>(processResult.StandardOutput, operation);
        foreach (var warning in payload.Errors ?? [])
        {
            ProcessRunner.ReportLog(callbacks, operation, LogLevel.Warning, warning);
        }

        var designCapacity = payload.Battery?.DesignCapacityMwh;
        var fullChargeCapacity = payload.Battery?.FullChargeCapacityMwh;
        double? wearPercent = designCapacity > 0 && fullChargeCapacity is not null
            ? Math.Max(0, Math.Round((1 - (fullChargeCapacity.Value / designCapacity.Value)) * 100, 1))
            : null;
        var totalGb = BytesToGb(payload.Storage?.TotalBytes);
        var freeGb = BytesToGb(payload.Storage?.FreeBytes);
        double? usedPercent = totalGb > 0 && freeGb is not null
            ? Math.Round(Math.Clamp((1 - (freeGb.Value / totalGb.Value)) * 100, 0, 100), 1)
            : null;

        var disks = (payload.Disks ?? [])
            .Select(disk => new PhysicalDiskInfo
            {
                Name = ValueOrUnavailable(disk.Name, "Unknown disk"),
                MediaType = ValueOrUnavailable(disk.MediaType, "Unspecified"),
                ReportedHealth = ValueOrUnavailable(disk.ReportedHealth, "Unknown"),
                WearPercent = disk.WearPercent,
                TemperatureCelsius = disk.TemperatureCelsius,
                Health = HealthAssessor.AssessDisk(disk.ReportedHealth, disk.WearPercent, disk.TemperatureCelsius)
            })
            .ToArray();

        var thermalPayload = payload.Thermal ?? new ThermalPayload();
        var snapshot = new DashboardSnapshot
        {
            CollectedAt = DateTimeOffset.Now,
            Device = new DeviceInfo
            {
                Manufacturer = ValueOrUnavailable(payload.Device?.Manufacturer),
                Model = ValueOrUnavailable(payload.Device?.Model),
                ProcessorName = ValueOrUnavailable(payload.Device?.ProcessorName),
                InstalledMemoryGb = payload.Device?.InstalledMemoryGb
            },
            OperatingSystem = new OperatingSystemInfo
            {
                Caption = ValueOrUnavailable(payload.OperatingSystem?.Caption),
                DisplayVersion = payload.OperatingSystem?.DisplayVersion ?? string.Empty,
                BuildNumber = payload.OperatingSystem?.BuildNumber ?? string.Empty,
                InstalledAt = ParseDate(payload.OperatingSystem?.InstalledAt),
                Uptime = payload.OperatingSystem?.UptimeSeconds is double uptimeSeconds
                    ? TimeSpan.FromSeconds(Math.Max(0, uptimeSeconds))
                    : null
            },
            Battery = new BatteryInfo
            {
                IsPresent = payload.Battery?.IsPresent ?? false,
                DesignCapacityMwh = designCapacity,
                FullChargeCapacityMwh = fullChargeCapacity,
                WearPercent = wearPercent,
                CycleCount = payload.Battery?.CycleCount,
                Health = HealthAssessor.AssessBattery(payload.Battery?.IsPresent ?? false, designCapacity, fullChargeCapacity)
            },
            Storage = new StorageInfo
            {
                Drive = payload.Storage?.Drive ?? "C:",
                TotalGb = totalGb,
                FreeGb = freeGb,
                UsedPercent = usedPercent,
                Health = HealthAssessor.AssessStorage(totalGb, freeGb)
            },
            Disks = disks,
            Thermal = new ThermalSummary
            {
                ThrottleEngagementCount = thermalPayload.ThrottleEngagementCount,
                FirmwareSpeedCapCount = thermalPayload.FirmwareSpeedCapCount,
                HardwareErrorCount = thermalPayload.HardwareErrorCount,
                CurrentZoneTemperaturesCelsius = thermalPayload.ZoneTemperaturesCelsius ?? [],
                Health = HealthAssessor.AssessThermal(
                    thermalPayload.ThrottleEngagementCount,
                    thermalPayload.FirmwareSpeedCapCount,
                    thermalPayload.HardwareErrorCount)
            }
        };

        ProcessRunner.ReportLog(callbacks, operation, LogLevel.Information, "Dashboard data collected.");
        return snapshot;
    }

    public async Task<PowerStatus> GetPowerStatusAsync(
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        const string operation = "Refresh power status";
        ProcessRunner.ReportLog(callbacks, operation, LogLevel.Command, "> powercfg.exe (read-only power probes)");
        var limitsTask = RunProcessAsync(
            "powercfg.exe",
            ["/q", "scheme_current", "sub_processor", "PROCTHROTTLEMAX"],
            operation,
            null,
            streamOutput: false,
            cancellationToken);
        var overlayTask = RunProcessAsync(
            "powercfg.exe",
            ["/getactiveoverlayscheme"],
            operation,
            null,
            streamOutput: false,
            cancellationToken);
        var planTask = RunProcessAsync(
            "powercfg.exe",
            ["/getactivescheme"],
            operation,
            null,
            streamOutput: false,
            cancellationToken);

        await Task.WhenAll(limitsTask, overlayTask, planTask).ConfigureAwait(false);
        var limitsResult = await limitsTask.ConfigureAwait(false);
        var overlayResult = await overlayTask.ConfigureAwait(false);
        var planResult = await planTask.ConfigureAwait(false);

        var limitsText = limitsResult.StandardOutput + Environment.NewLine + limitsResult.StandardError;
        var acPercent = ParseHexSetting(limitsText, AcSettingPattern());
        var dcPercent = ParseHexSetting(limitsText, DcSettingPattern());
        var overlayText = overlayResult.StandardOutput + " " + overlayResult.StandardError;
        var overlayGuid = GuidPattern().Match(overlayText) is { Success: true } overlayMatch
            ? overlayMatch.Value.ToLowerInvariant()
            : null;
        var (overlay, overlayName) = MapOverlay(overlayGuid);
        var planText = planResult.StandardOutput + " " + planResult.StandardError;
        var planMatch = ActivePlanPattern().Match(planText);
        var planGuid = planMatch.Success ? planMatch.Groups["guid"].Value.ToLowerInvariant() : null;
        var planName = planMatch.Success
            ? ValueOrUnavailable(planMatch.Groups["name"].Value.Trim(), "Active power plan")
            : "Unavailable";

        var turboBoost = acPercent is null || dcPercent is null
            ? TurboBoostState.Unavailable
            : acPercent <= 99 && dcPercent <= 99
                ? TurboBoostState.Disabled
                : acPercent <= 99 || dcPercent <= 99
                    ? TurboBoostState.PartiallyCapped
                    : TurboBoostState.Enabled;

        ProcessRunner.ReportLog(
            callbacks,
            operation,
            limitsResult.Succeeded && planResult.Succeeded ? LogLevel.Information : LogLevel.Warning,
            limitsResult.Succeeded && planResult.Succeeded
                ? "Power status collected."
                : "Some power settings are not exposed by this Windows power model.");

        return new PowerStatus
        {
            CollectedAt = DateTimeOffset.Now,
            AcMaximumProcessorPercent = acPercent,
            DcMaximumProcessorPercent = dcPercent,
            TurboBoost = turboBoost,
            ActiveOverlay = overlay,
            ActiveOverlayName = overlayName,
            ActiveOverlayGuid = overlayGuid,
            ActivePlanName = planName,
            ActivePlanGuid = planGuid,
            Health = HealthAssessor.AssessPower(acPercent, dcPercent)
        };
    }

    public async Task<IReadOnlyList<ThermalEventRecord>> ScanThermalEventsAsync(
        int days,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        if (days is < 1 or > 3650)
        {
            throw new ArgumentOutOfRangeException(nameof(days), "The event window must be between 1 and 3650 days.");
        }

        const string operation = "Scan thermal events";
        var script = $$"""
            $since = (Get-Date).AddDays(-{{days}})
            $seen = New-Object 'System.Collections.Generic.HashSet[string]'
            $rows = @()
            $targets = @(
                @{ Provider = 'Microsoft-Windows-Kernel-Power'; Id = 125; Kind = 'ThermalThrottle' },
                @{ Provider = 'Microsoft-Windows-Kernel-Processor-Power'; Id = 37; Kind = 'FirmwareSpeedCap' },
                @{ Provider = 'Microsoft-Windows-WHEA-Logger'; Id = $null; Kind = 'HardwareError' }
            )
            foreach ($target in $targets) {
                $filter = @{ LogName = 'System'; ProviderName = $target.Provider; StartTime = $since }
                if ($null -ne $target.Id) { $filter.Id = $target.Id }
                try { $events = @(Get-WinEvent -FilterHashtable $filter -ErrorAction Stop) } catch { $events = @() }
                foreach ($eventRecord in $events) {
                    if (-not $seen.Add([string]$eventRecord.RecordId)) { continue }
                    $message = ([string]$eventRecord.Message -replace '\s+', ' ').Trim()
                    if ($message.Length -gt 500) { $message = $message.Substring(0, 500) + '...' }
                    $rows += [ordered]@{
                        Timestamp = $eventRecord.TimeCreated.ToString('o')
                        Level = [string]$eventRecord.LevelDisplayName
                        Provider = ([string]$eventRecord.ProviderName -replace '^Microsoft-Windows-', '')
                        EventId = [int]$eventRecord.Id
                        RecordId = [long]$eventRecord.RecordId
                        Message = $message
                        Kind = $target.Kind
                    }
                }
            }
            try {
                $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; Level = 1, 2, 3; StartTime = $since } -MaxEvents 3000 -ErrorAction Stop)
            } catch { $events = @() }
            foreach ($eventRecord in $events) {
                if (-not $eventRecord.Message -or $eventRecord.Message -notmatch '(?i)thermal|throttl|overheat|temperature') { continue }
                if (-not $seen.Add([string]$eventRecord.RecordId)) { continue }
                $message = ([string]$eventRecord.Message -replace '\s+', ' ').Trim()
                if ($message.Length -gt 500) { $message = $message.Substring(0, 500) + '...' }
                $rows += [ordered]@{
                    Timestamp = $eventRecord.TimeCreated.ToString('o')
                    Level = [string]$eventRecord.LevelDisplayName
                    Provider = ([string]$eventRecord.ProviderName -replace '^Microsoft-Windows-', '')
                    EventId = [int]$eventRecord.Id
                    RecordId = [long]$eventRecord.RecordId
                    Message = $message
                    Kind = 'ThermalWarning'
                }
            }
            ConvertTo-Json -InputObject @($rows | Sort-Object Timestamp -Descending) -Depth 4 -Compress
            """;

        var result = await _powerShell.RunAsync(
            script,
            operation,
            callbacks,
            streamOutput: false,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, operation);
        var payload = DeserializeJson<ThermalEventPayload[]>(result.StandardOutput, operation) ?? [];
        var records = payload.Select(item => new ThermalEventRecord
        {
            Timestamp = ParseDate(item.Timestamp) ?? DateTimeOffset.MinValue,
            Level = item.Level ?? string.Empty,
            Provider = item.Provider ?? string.Empty,
            EventId = item.EventId,
            RecordId = item.RecordId,
            Message = item.Message ?? string.Empty,
            Kind = Enum.TryParse<ThermalEventKind>(item.Kind, ignoreCase: true, out var kind)
                    ? kind
                    : ThermalEventKind.ThermalWarning
        })
            .OrderByDescending(item => item.Timestamp)
            .ToArray();
        ProcessRunner.ReportLog(callbacks, operation, LogLevel.Information, $"Found {records.Length} thermal or hardware event(s) in the last {days} days.");
        return records;
    }

    public async Task<IReadOnlyList<PackageRecord>> SearchPackagesAsync(
        string query,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Enter a package name or identifier.", nameof(query));
        }

        var operation = $"Search packages for {query.Trim()}";
        var result = await RunProcessAsync(
            "winget.exe",
            ["search", query.Trim(), "--accept-source-agreements", "--disable-interactivity"],
            operation,
            callbacks,
            streamOutput: false,
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded && !IsNoPackageResult(result))
        {
            EnsureSuccess(result, operation);
        }
        var packages = WingetTableParser.Parse(result.StandardOutput);
        ProcessRunner.ReportLog(callbacks, operation, LogLevel.Information, $"Found {packages.Count} package(s).");
        return packages;
    }

    public async Task<IReadOnlyList<PackageRecord>> GetPackageUpdatesAsync(
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        const string operation = "Check package updates";
        var result = await RunProcessAsync(
            "winget.exe",
            ["upgrade", "--include-unknown", "--accept-source-agreements", "--disable-interactivity"],
            operation,
            callbacks,
            streamOutput: false,
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded && !IsNoPackageResult(result))
        {
            EnsureSuccess(result, operation);
        }
        var packages = WingetTableParser.Parse(result.StandardOutput);
        ProcessRunner.ReportLog(callbacks, operation, LogLevel.Information, $"Found {packages.Count} available update(s).");
        return packages;
    }

    public Task<OperationResult> InstallPackagesAsync(
        IEnumerable<string> packageIds,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default) =>
        RunPackageActionAsync("install", "Install packages", packageIds, callbacks, cancellationToken);

    public Task<OperationResult> UpgradePackagesAsync(
        IEnumerable<string> packageIds,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default) =>
        RunPackageActionAsync("upgrade", "Upgrade packages", packageIds, callbacks, cancellationToken);

    public async Task<OperationResult> UpgradeAllPackagesAsync(
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        const string operation = "Upgrade all packages";
        var startedAt = DateTimeOffset.Now;
        var result = await RunProcessAsync(
            "winget.exe",
            ["upgrade", "--all", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"],
            operation,
            callbacks,
            streamOutput: true,
            cancellationToken).ConfigureAwait(false);
        return ToOperationResult(operation, result, startedAt, result.Succeeded ? "All eligible packages were upgraded." : "One or more packages could not be upgraded.");
    }

    public async Task<PowerStatus> SetCpuMaximumAsync(
        int percent,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        if (percent is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), "The processor maximum must be between 1 and 100 percent.");
        }

        const string operation = "Set processor power limit";
        await RunRequiredAsync(
            "powercfg.exe",
            ["/setacvalueindex", "scheme_current", "sub_processor", "PROCTHROTTLEMAX", percent.ToString(CultureInfo.InvariantCulture)],
            operation,
            callbacks,
            cancellationToken).ConfigureAwait(false);
        await RunRequiredAsync(
            "powercfg.exe",
            ["/setdcvalueindex", "scheme_current", "sub_processor", "PROCTHROTTLEMAX", percent.ToString(CultureInfo.InvariantCulture)],
            operation,
            callbacks,
            cancellationToken).ConfigureAwait(false);
        await RunRequiredAsync(
            "powercfg.exe",
            ["/setactive", "scheme_current"],
            operation,
            callbacks,
            cancellationToken).ConfigureAwait(false);
        ProcessRunner.ReportLog(callbacks, operation, LogLevel.Information, $"Processor maximum set to {percent}% on AC and battery power.");
        return await GetPowerStatusAsync(callbacks, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PowerStatus> SetPowerOverlayAsync(
        PowerOverlay overlay,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        const string operation = "Set power mode";
        var guid = overlay switch
        {
            PowerOverlay.BestPowerEfficiency => EfficiencyOverlayGuid,
            PowerOverlay.BetterPerformance => BetterPerformanceOverlayGuid,
            PowerOverlay.BestPerformance => PerformanceOverlayGuid,
            _ => BalancedOverlayGuid
        };
        await RunRequiredAsync(
            "powercfg.exe",
            ["/overlaysetactive", guid],
            operation,
            callbacks,
            cancellationToken).ConfigureAwait(false);
        ProcessRunner.ReportLog(callbacks, operation, LogLevel.Information, $"Power mode set to {GetOverlayName(overlay)}.");
        return await GetPowerStatusAsync(callbacks, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GenerateBatteryReportAsync(
        string? destinationPath = null,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        const string operation = "Generate battery report";
        destinationPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "SurfaceMedic-battery-report.html");
        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("The battery report destination must include a valid directory.", nameof(destinationPath));
        }

        Directory.CreateDirectory(directory);
        await RunRequiredAsync(
            "powercfg.exe",
            ["/batteryreport", "/output", fullPath],
            operation,
            callbacks,
            cancellationToken).ConfigureAwait(false);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException("Windows completed the battery report command but did not create the report file.");
        }

        ProcessRunner.ReportLog(callbacks, operation, LogLevel.Information, $"Battery report saved to {fullPath}.");
        return fullPath;
    }

    public async Task<OperationResult> RunMaintenanceAsync(
        MaintenanceOperation operation,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        var operationName = GetMaintenanceName(operation);
        var startedAt = DateTimeOffset.Now;
        ProcessResult result;
        if (operation == MaintenanceOperation.CleanTemporaryFiles)
        {
            const string script = """
                $targets = @($env:TEMP, (Join-Path $env:windir 'Temp')) | Select-Object -Unique
                [long]$totalFreed = 0
                foreach ($target in $targets) {
                    if (-not (Test-Path -LiteralPath $target -PathType Container)) { continue }
                    [long]$before = (Get-ChildItem -LiteralPath $target -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
                    Get-ChildItem -LiteralPath $target -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
                    [long]$after = (Get-ChildItem -LiteralPath $target -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
                    $totalFreed += [math]::Max(0, ($before - $after))
                }
                "Freed {0:N1} MB of temporary files." -f ($totalFreed / 1MB)
                """;
            result = await _powerShell.RunAsync(
                script,
                operationName,
                callbacks,
                streamOutput: true,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var (fileName, arguments) = GetMaintenanceCommand(operation);
            result = await RunProcessAsync(
                fileName,
                arguments,
                operationName,
                callbacks,
                streamOutput: true,
                cancellationToken).ConfigureAwait(false);
        }

        var summary = result.Succeeded
            ? LastNonEmptyLine(result.StandardOutput) ?? $"{operationName} completed."
            : $"{operationName} exited with code {result.ExitCode}.";
        return ToOperationResult(operationName, result, startedAt, summary);
    }

    private async Task<OperationResult> RunPackageActionAsync(
        string verb,
        string operation,
        IEnumerable<string> packageIds,
        OperationCallbacks? callbacks,
        CancellationToken cancellationToken)
    {
        EnsureWindows();
        var ids = packageIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (ids.Length == 0)
        {
            throw new ArgumentException("Select at least one package.", nameof(packageIds));
        }

        var startedAt = DateTimeOffset.Now;
        var failedCount = 0;
        var exitCode = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessRunner.ReportLog(callbacks, operation, LogLevel.Information, $"{(verb == "install" ? "Installing" : "Upgrading")} {id}.");
            var result = await RunProcessAsync(
                "winget.exe",
                [verb, "--id", id, "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"],
                operation,
                callbacks,
                streamOutput: true,
                cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                failedCount++;
                exitCode = result.ExitCode;
            }
        }

        var succeeded = failedCount == 0;
        return new OperationResult
        {
            Operation = operation,
            Succeeded = succeeded,
            ExitCode = exitCode,
            Summary = succeeded
                ? $"{ids.Length} package(s) completed successfully."
                : $"{failedCount} of {ids.Length} package(s) failed.",
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.Now
        };
    }

    private Task<ProcessResult> RunProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string operation,
        OperationCallbacks? callbacks,
        bool streamOutput,
        CancellationToken cancellationToken) =>
        _processRunner.RunAsync(
            fileName,
            arguments,
            operation,
            callbacks,
            streamOutput,
            filterProgressNoise: true,
            commandDescription: null,
            cancellationToken);

    private async Task RunRequiredAsync(
        string fileName,
        IEnumerable<string> arguments,
        string operation,
        OperationCallbacks? callbacks,
        CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            fileName,
            arguments,
            operation,
            callbacks,
            streamOutput: true,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, operation);
    }

    private static void EnsureSuccess(ProcessResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var detail = LastNonEmptyLine(result.StandardError) ?? LastNonEmptyLine(result.StandardOutput);
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(detail)
                ? $"{operation} failed with exit code {result.ExitCode}."
                : $"{operation} failed with exit code {result.ExitCode}: {detail}");
    }

    private static OperationResult ToOperationResult(
        string operation,
        ProcessResult result,
        DateTimeOffset startedAt,
        string summary) =>
        new()
        {
            Operation = operation,
            Succeeded = result.Succeeded,
            ExitCode = result.ExitCode,
            Summary = summary,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.Now
        };

    private static T DeserializeJson<T>(string output, string operation)
    {
        var payload = output.Trim().TrimStart('\uFEFF');
        try
        {
            var result = JsonSerializer.Deserialize<T>(payload, JsonOptions);
            return result ?? throw new JsonException("The response was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{operation} returned data that could not be read.", exception);
        }
    }

    private static int? ParseHexSetting(string text, Regex pattern)
    {
        var match = pattern.Match(text);
        return match.Success && int.TryParse(
            match.Groups["value"].Value,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static (PowerOverlay Overlay, string Name) MapOverlay(string? guid) => guid switch
    {
        EfficiencyOverlayGuid => (PowerOverlay.BestPowerEfficiency, "Best power efficiency"),
        BetterPerformanceOverlayGuid => (PowerOverlay.BetterPerformance, "Better performance"),
        PerformanceOverlayGuid => (PowerOverlay.BestPerformance, "Best performance"),
        BalancedOverlayGuid => (PowerOverlay.Balanced, "Balanced"),
        null => (PowerOverlay.Balanced, "Balanced (no overlay reported)"),
        _ => (PowerOverlay.Balanced, $"Custom overlay ({guid})")
    };

    private static string GetOverlayName(PowerOverlay overlay) => overlay switch
    {
        PowerOverlay.BestPowerEfficiency => "Best power efficiency",
        PowerOverlay.BetterPerformance => "Better performance",
        PowerOverlay.BestPerformance => "Best performance",
        _ => "Balanced"
    };

    private static string GetMaintenanceName(MaintenanceOperation operation) => operation switch
    {
        MaintenanceOperation.CleanTemporaryFiles => "Clean temporary files",
        MaintenanceOperation.SystemFileCheck => "System File Checker",
        MaintenanceOperation.ScanComponentStore => "Scan component store",
        MaintenanceOperation.RepairComponentStore => "Repair component store",
        MaintenanceOperation.CleanupComponentStore => "Clean component store",
        MaintenanceOperation.FlushDns => "Flush DNS cache",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown maintenance operation.")
    };

    private static (string FileName, string[] Arguments) GetMaintenanceCommand(MaintenanceOperation operation) => operation switch
    {
        MaintenanceOperation.SystemFileCheck => ("sfc.exe", ["/scannow"]),
        MaintenanceOperation.ScanComponentStore => ("dism.exe", ["/Online", "/Cleanup-Image", "/ScanHealth"]),
        MaintenanceOperation.RepairComponentStore => ("dism.exe", ["/Online", "/Cleanup-Image", "/RestoreHealth"]),
        MaintenanceOperation.CleanupComponentStore => ("dism.exe", ["/Online", "/Cleanup-Image", "/StartComponentCleanup"]),
        MaintenanceOperation.FlushDns => ("ipconfig.exe", ["/flushdns"]),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "This maintenance operation has no native command.")
    };

    private static string? LastNonEmptyLine(string text) => text
        .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .LastOrDefault();

    private static bool IsNoPackageResult(ProcessResult result)
    {
        var output = result.StandardOutput + Environment.NewLine + result.StandardError;
        return output.Contains("No package found", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("No installed package found", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("No applicable update found", StringComparison.OrdinalIgnoreCase);
    }

    private static double? BytesToGb(double? bytes) => bytes is null ? null : Math.Round(bytes.Value / (1024 * 1024 * 1024), 1);

    private static string ValueOrUnavailable(string? value, string fallback = "Unavailable") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("SurfaceMedic system operations require Windows.");
        }
    }

    [GeneratedRegex(@"Current AC Power Setting Index:\s*0x(?<value>[0-9a-fA-F]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AcSettingPattern();

    [GeneratedRegex(@"Current DC Power Setting Index:\s*0x(?<value>[0-9a-fA-F]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DcSettingPattern();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"Power Scheme GUID:\s*(?<guid>[0-9a-fA-F-]{36})(?:\s*\((?<name>[^)]*)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex ActivePlanPattern();

    private sealed record DashboardPayload
    {
        public DevicePayload? Device { get; init; }
        public OperatingSystemPayload? OperatingSystem { get; init; }
        public BatteryPayload? Battery { get; init; }
        public StoragePayload? Storage { get; init; }
        public PhysicalDiskPayload[]? Disks { get; init; }
        public ThermalPayload? Thermal { get; init; }
        public string[]? Errors { get; init; }
    }

    private sealed record DevicePayload
    {
        public string? Manufacturer { get; init; }
        public string? Model { get; init; }
        public string? ProcessorName { get; init; }
        public double? InstalledMemoryGb { get; init; }
    }

    private sealed record OperatingSystemPayload
    {
        public string? Caption { get; init; }
        public string? DisplayVersion { get; init; }
        public string? BuildNumber { get; init; }
        public string? InstalledAt { get; init; }
        public double? UptimeSeconds { get; init; }
    }

    private sealed record BatteryPayload
    {
        public bool IsPresent { get; init; }
        public double? DesignCapacityMwh { get; init; }
        public double? FullChargeCapacityMwh { get; init; }
        public int? CycleCount { get; init; }
    }

    private sealed record StoragePayload
    {
        public string? Drive { get; init; }
        public double? TotalBytes { get; init; }
        public double? FreeBytes { get; init; }
    }

    private sealed record PhysicalDiskPayload
    {
        public string? Name { get; init; }
        public string? MediaType { get; init; }
        public string? ReportedHealth { get; init; }
        public double? WearPercent { get; init; }
        public double? TemperatureCelsius { get; init; }
    }

    private sealed record ThermalPayload
    {
        public int ThrottleEngagementCount { get; init; }
        public int FirmwareSpeedCapCount { get; init; }
        public int HardwareErrorCount { get; init; }
        public double[]? ZoneTemperaturesCelsius { get; init; }
    }

    private sealed record ThermalEventPayload
    {
        public string? Timestamp { get; init; }
        public string? Level { get; init; }
        public string? Provider { get; init; }
        public int EventId { get; init; }
        public long? RecordId { get; init; }
        public string? Message { get; init; }
        public string? Kind { get; init; }
    }
}
