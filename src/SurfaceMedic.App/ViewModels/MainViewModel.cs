using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using SurfaceMedic.App.Infrastructure;
using SurfaceMedic.Core.Models;
using SurfaceMedic.Core.Services;

namespace SurfaceMedic.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ISurfaceMedicService _service;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly string _sessionLogPath;
    private readonly object _logLock = new();
    private readonly List<AsyncRelayCommand> _asyncCommands = [];
    private NavigationItem _selectedNavigation;
    private AppPage _currentPage;
    private bool _isBusy;
    private string _busyMessage = "Ready";
    private string _statusText = "Ready";
    private string _logText = string.Empty;
    private bool _isToastVisible;
    private string _toastTitle = "Ready";
    private string _toastMessage = string.Empty;
    private string _toastGlyph = "\uE73E";
    private long _toastSequence;
    private bool _isActivityLogExpanded;
    private string _healthTitle = "System health";
    private string _healthMessage = "Collecting health signals...";
    private string _healthDetail = "Diagnostics are running";
    private string _attentionMessage = "Checking for items that need attention.";
    private string _attentionDetail = "No assessment yet";
    private string _concernTitle = "Analysis in progress";
    private string _concernMessage = "SurfaceMedic is reviewing this PC.";
    private int _thermalDays = 30;
    private bool _isThermalLoading;
    private string _thermalSummaryTitle = "Thermal history is ready to scan";
    private string _thermalSummary = "Choose a range to search the local System event log.";
    private ThermalEventRecord? _selectedThermalEvent;
    private string _cpuModeLabel = "Reading status";
    private string _cpuStatusText = "Collecting processor ceiling and Turbo Boost state...";
    private double _cpuAcPercent;
    private double _cpuDcPercent;
    private string _cpuAcLabel = "--";
    private string _cpuDcLabel = "--";
    private string _powerModeText = "Reading active mode...";
    private string _activePowerPlan = "Reading active power plan...";
    private string _searchQuery = string.Empty;
    private PackageRecord? _selectedPackage;
    private PackageRecord? _selectedPackageUpdate;

    public MainViewModel(ISurfaceMedicService service, bool isAdministrator, SettingsService settingsService)
    {
        _service = service;
        _settingsService = settingsService;
        _settings = settingsService.Load();
        _isActivityLogExpanded = _settings.ActivityLogExpanded;
        IsAdministrator = isAdministrator;

        AppPaths.EnsureDirectories();
        _sessionLogPath = Path.Combine(AppPaths.LogsDirectory, $"session-{DateTime.Now:yyyyMMdd}.log");

        PrimaryNavigation =
        [
            new(AppPage.Overview, "Overview", "\uE80F", "Device overview", "Health, capacity, and thermal signals at a glance"),
            new(AppPage.Thermal, "Thermal", "\uE7A3", "Thermal history", "Throttle, firmware cap, and hardware events"),
            new(AppPage.Power, "Power", "\uE945", "Power controls", "Tune processor limits and Windows power mode"),
            new(AppPage.Software, "Software", "\uE71E", "Software maintenance", "Search, install, and update packages with winget"),
            new(AppPage.Maintenance, "Maintenance", "\uE90F", "Windows maintenance", "Repair, cleanup, firmware, and recovery tools")
        ];
        SecondaryNavigation =
        [
            new(AppPage.Settings, "Settings", "\uE713", "Settings", "Appearance, activity, and diagnostic storage"),
            new(AppPage.About, "About", "\uE946", "About SurfaceMedic", "Version, runtime, privacy, and license")
        ];
        _selectedNavigation = PrimaryNavigation[0];
        _currentPage = AppPage.Overview;

        OverviewCards =
        [
            new() { Title = "Device", Glyph = "\uE7F8", TargetPage = AppPage.About },
            new() { Title = "Operating system", Glyph = "\uE782", TargetPage = AppPage.Maintenance },
            new() { Title = "Battery health", Glyph = "\uE850", TargetPage = AppPage.Power },
            new() { Title = "Storage", Glyph = "\uEDA2", TargetPage = AppPage.Maintenance },
            new() { Title = "Disk health", Glyph = "\uEDA2", TargetPage = AppPage.Maintenance },
            new() { Title = "Thermal activity", Glyph = "\uE7A3", TargetPage = AppPage.Thermal }
        ];

        RefreshDashboardCommand = Register(new AsyncRelayCommand(_ => RefreshDashboardAsync(), _ => !IsBusy, HandleCommandError));
        NavigateCommand = new RelayCommand(Navigate);
        ToggleActivityLogCommand = new RelayCommand(_ => IsActivityLogExpanded = !IsActivityLogExpanded);
        CopyLogCommand = new RelayCommand(_ => CopyLog(), _ => !string.IsNullOrWhiteSpace(LogText));
        ClearLogCommand = new RelayCommand(_ => ClearLog(), _ => !string.IsNullOrWhiteSpace(LogText));
        SelectThermalRangeCommand = new RelayCommand(SelectThermalRange);
        ScanThermalCommand = Register(new AsyncRelayCommand(_ => ScanThermalAsync(), _ => !IsBusy, HandleCommandError));
        ExportThermalCommand = Register(new AsyncRelayCommand(_ => ExportThermalAsync(), _ => !IsBusy && ThermalEvents.Count > 0, HandleCommandError));
        RefreshPowerCommand = Register(new AsyncRelayCommand(_ => RefreshPowerAsync(), _ => !IsBusy, HandleCommandError));
        SetCpuCapCommand = Register(new AsyncRelayCommand(SetCpuCapAsync, _ => !IsBusy, HandleCommandError));
        SetPowerModeCommand = Register(new AsyncRelayCommand(SetPowerModeAsync, _ => !IsBusy, HandleCommandError));
        GenerateBatteryReportCommand = Register(new AsyncRelayCommand(_ => GenerateBatteryReportAsync(), _ => !IsBusy, HandleCommandError));
        SearchPackagesCommand = Register(new AsyncRelayCommand(_ => SearchPackagesAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(SearchQuery), HandleCommandError));
        InstallSelectedPackageCommand = Register(new AsyncRelayCommand(_ => InstallSelectedPackageAsync(), _ => !IsBusy && SelectedPackage is not null, HandleCommandError));
        InstallRecommendedToolCommand = Register(new AsyncRelayCommand(InstallRecommendedToolAsync, _ => !IsBusy, HandleCommandError));
        CheckUpdatesCommand = Register(new AsyncRelayCommand(_ => CheckUpdatesAsync(), _ => !IsBusy, HandleCommandError));
        UpgradeSelectedPackageCommand = Register(new AsyncRelayCommand(_ => UpgradeSelectedPackageAsync(), _ => !IsBusy && SelectedPackageUpdate is not null, HandleCommandError));
        UpgradeAllPackagesCommand = Register(new AsyncRelayCommand(_ => UpgradeAllPackagesAsync(), _ => !IsBusy, HandleCommandError));
        RunMaintenanceCommand = Register(new AsyncRelayCommand(RunMaintenanceAsync, _ => !IsBusy, HandleCommandError));
        OpenWindowsUpdateCommand = new RelayCommand(_ => OpenUri("ms-settings:windowsupdate", "Opening Windows Update"));
        OpenSurfaceAppCommand = new RelayCommand(_ => OpenUri("ms-windows-store://pdp/?ProductId=9WZDNCRFJB8P", "Opening the Surface app"));
        OpenLogsFolderCommand = new RelayCommand(_ => OpenPath(AppPaths.LogsDirectory));

        ThemeManager.Apply(_settings.Theme);
        AppendLog(LogLevel.Information, "Startup", $"SurfaceMedic {VersionLabel} session started.");
        if (!isAdministrator)
        {
            AppendLog(LogLevel.Warning, "Startup", "Running without elevation. Repair and power actions require administrator access.");
        }
    }

    public IReadOnlyList<NavigationItem> PrimaryNavigation { get; }
    public IReadOnlyList<NavigationItem> SecondaryNavigation { get; }
    public ObservableCollection<OverviewCard> OverviewCards { get; }
    public ObservableCollection<ThermalEventRecord> ThermalEvents { get; } = [];
    public ObservableCollection<PackageRecord> Packages { get; } = [];
    public ObservableCollection<PackageRecord> PackageUpdates { get; } = [];

    public string VersionLabel { get; } = $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.1"}";
    public bool IsAdministrator { get; }
    public string PrivilegeLabel => IsAdministrator ? "Administrator" : "Limited access";
    public string LogsDirectory => AppPaths.LogsDirectory;

    public NavigationItem SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            if (value is null || !SetProperty(ref _selectedNavigation, value))
            {
                return;
            }

            CurrentPage = value.Page;
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageSubtitle));
            OnPropertyChanged(nameof(SelectedPrimaryNavigation));
            OnPropertyChanged(nameof(SelectedSecondaryNavigation));
        }
    }

    public AppPage CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string PageTitle => SelectedNavigation.Title;
    public string PageSubtitle => SelectedNavigation.Subtitle;

    public NavigationItem? SelectedPrimaryNavigation
    {
        get => PrimaryNavigation.Contains(SelectedNavigation) ? SelectedNavigation : null;
        set { if (value is not null) SelectedNavigation = value; }
    }

    public NavigationItem? SelectedSecondaryNavigation
    {
        get => SecondaryNavigation.Contains(SelectedNavigation) ? SelectedNavigation : null;
        set { if (value is not null) SelectedNavigation = value; }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            RaiseCommandStates();
        }
    }

    public string BusyMessage
    {
        get => _busyMessage;
        private set => SetProperty(ref _busyMessage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string LogText
    {
        get => _logText;
        private set
        {
            if (SetProperty(ref _logText, value))
            {
                (CopyLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }

    public string ToastTitle { get => _toastTitle; private set => SetProperty(ref _toastTitle, value); }
    public string ToastMessage { get => _toastMessage; private set => SetProperty(ref _toastMessage, value); }
    public string ToastGlyph { get => _toastGlyph; private set => SetProperty(ref _toastGlyph, value); }

    public bool IsActivityLogExpanded
    {
        get => _isActivityLogExpanded;
        set
        {
            if (!SetProperty(ref _isActivityLogExpanded, value))
            {
                return;
            }

            _settings.ActivityLogExpanded = value;
            _settingsService.Save(_settings);
            OnPropertyChanged(nameof(ActivityLogHeight));
            OnPropertyChanged(nameof(ActivityLogStateLabel));
        }
    }

    public GridLength ActivityLogHeight => IsActivityLogExpanded ? new(214) : new(42);
    public string ActivityLogStateLabel => IsActivityLogExpanded ? "Expanded" : "Collapsed";

    public string HealthTitle { get => _healthTitle; private set => SetProperty(ref _healthTitle, value); }
    public string HealthMessage { get => _healthMessage; private set => SetProperty(ref _healthMessage, value); }
    public string HealthDetail { get => _healthDetail; private set => SetProperty(ref _healthDetail, value); }
    public string AttentionMessage { get => _attentionMessage; private set => SetProperty(ref _attentionMessage, value); }
    public string AttentionDetail { get => _attentionDetail; private set => SetProperty(ref _attentionDetail, value); }
    public string ConcernTitle { get => _concernTitle; private set => SetProperty(ref _concernTitle, value); }
    public string ConcernMessage { get => _concernMessage; private set => SetProperty(ref _concernMessage, value); }

    public bool IsThermalLoading { get => _isThermalLoading; private set => SetProperty(ref _isThermalLoading, value); }
    public string ThermalSummaryTitle { get => _thermalSummaryTitle; private set => SetProperty(ref _thermalSummaryTitle, value); }
    public string ThermalSummary { get => _thermalSummary; private set => SetProperty(ref _thermalSummary, value); }
    public string ThermalEventCountText => $"{ThermalEvents.Count} event{(ThermalEvents.Count == 1 ? string.Empty : "s")}";
    public ThermalEventRecord? SelectedThermalEvent { get => _selectedThermalEvent; set => SetProperty(ref _selectedThermalEvent, value); }

    public string CpuModeLabel { get => _cpuModeLabel; private set => SetProperty(ref _cpuModeLabel, value); }
    public string CpuStatusText { get => _cpuStatusText; private set => SetProperty(ref _cpuStatusText, value); }
    public double CpuAcPercent { get => _cpuAcPercent; private set => SetProperty(ref _cpuAcPercent, value); }
    public double CpuDcPercent { get => _cpuDcPercent; private set => SetProperty(ref _cpuDcPercent, value); }
    public string CpuAcLabel { get => _cpuAcLabel; private set => SetProperty(ref _cpuAcLabel, value); }
    public string CpuDcLabel { get => _cpuDcLabel; private set => SetProperty(ref _cpuDcLabel, value); }
    public string PowerModeText { get => _powerModeText; private set => SetProperty(ref _powerModeText, value); }
    public string ActivePowerPlan { get => _activePowerPlan; private set => SetProperty(ref _activePowerPlan, value); }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public PackageRecord? SelectedPackage
    {
        get => _selectedPackage;
        set
        {
            if (SetProperty(ref _selectedPackage, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public PackageRecord? SelectedPackageUpdate
    {
        get => _selectedPackageUpdate;
        set
        {
            if (SetProperty(ref _selectedPackageUpdate, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsDarkTheme
    {
        get => !_settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase);
        set { if (value) ApplyTheme("Dark"); }
    }

    public bool IsLightTheme
    {
        get => _settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase);
        set { if (value) ApplyTheme("Light"); }
    }

    public System.Windows.Input.ICommand RefreshDashboardCommand { get; }
    public System.Windows.Input.ICommand NavigateCommand { get; }
    public System.Windows.Input.ICommand ToggleActivityLogCommand { get; }
    public System.Windows.Input.ICommand CopyLogCommand { get; }
    public System.Windows.Input.ICommand ClearLogCommand { get; }
    public System.Windows.Input.ICommand SelectThermalRangeCommand { get; }
    public System.Windows.Input.ICommand ScanThermalCommand { get; }
    public System.Windows.Input.ICommand ExportThermalCommand { get; }
    public System.Windows.Input.ICommand RefreshPowerCommand { get; }
    public System.Windows.Input.ICommand SetCpuCapCommand { get; }
    public System.Windows.Input.ICommand SetPowerModeCommand { get; }
    public System.Windows.Input.ICommand GenerateBatteryReportCommand { get; }
    public System.Windows.Input.ICommand SearchPackagesCommand { get; }
    public System.Windows.Input.ICommand InstallSelectedPackageCommand { get; }
    public System.Windows.Input.ICommand InstallRecommendedToolCommand { get; }
    public System.Windows.Input.ICommand CheckUpdatesCommand { get; }
    public System.Windows.Input.ICommand UpgradeSelectedPackageCommand { get; }
    public System.Windows.Input.ICommand UpgradeAllPackagesCommand { get; }
    public System.Windows.Input.ICommand RunMaintenanceCommand { get; }
    public System.Windows.Input.ICommand OpenWindowsUpdateCommand { get; }
    public System.Windows.Input.ICommand OpenSurfaceAppCommand { get; }
    public System.Windows.Input.ICommand OpenLogsFolderCommand { get; }

    public async Task InitializeAsync()
    {
        await RunBusyAsync("Collecting device diagnostics", async () =>
        {
            var dashboardTask = _service.GetDashboardAsync(CreateCallbacks());
            var powerTask = _service.GetPowerStatusAsync(CreateCallbacks());
            await Task.WhenAll(dashboardTask, powerTask);
            ApplyDashboard(await dashboardTask);
            ApplyPowerStatus(await powerTask);
            StatusText = "Ready";
        });
    }

    public void NavigateForCapture(AppPage page) => Navigate(page);

    internal void PrepareThermalCaptureState()
    {
        ThermalEvents.Clear();
        ThermalEvents.Add(new ThermalEventRecord
        {
            Timestamp = new DateTimeOffset(2026, 7, 10, 10, 42, 0, TimeSpan.Zero),
            Level = "Warning",
            Provider = "Kernel-Processor-Power",
            EventId = 37,
            Kind = ThermalEventKind.FirmwareSpeedCap,
            Message = "Processor performance was limited by system firmware."
        });
        ThermalEvents.Add(new ThermalEventRecord
        {
            Timestamp = new DateTimeOffset(2026, 7, 10, 10, 37, 0, TimeSpan.Zero),
            Level = "Error",
            Provider = "WHEA-Logger",
            EventId = 17,
            Kind = ThermalEventKind.HardwareError,
            Message = "A corrected hardware error was reported."
        });
        ThermalSummaryTitle = "2 sample events shown";
        ThermalSummary = "Populated-state preview for offscreen interface verification.";
        OnPropertyChanged(nameof(ThermalEventCountText));
    }

    internal void ResetThermalCaptureState()
    {
        ThermalEvents.Clear();
        ThermalSummaryTitle = "Thermal history is ready to scan";
        ThermalSummary = "Choose a range to search the local System event log.";
        OnPropertyChanged(nameof(ThermalEventCountText));
    }

    public void ApplyThemeForCapture(string theme)
    {
        _settings.Theme = theme;
        ThemeManager.Apply(theme);
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        IsToastVisible = false;
    }

    private AsyncRelayCommand Register(AsyncRelayCommand command)
    {
        _asyncCommands.Add(command);
        return command;
    }

    private void RaiseCommandStates()
    {
        foreach (var command in _asyncCommands)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private OperationCallbacks CreateCallbacks() => new(
        new Progress<OperationProgress>(progress => BusyMessage = progress.Message),
        new Progress<LogEntry>(entry => AppendLog(entry.Level, entry.Operation, entry.Message)));

    private async Task RunBusyAsync(string message, Func<Task> operation)
    {
        IsBusy = true;
        BusyMessage = message;
        StatusText = "Working";
        try
        {
            await operation();
        }
        finally
        {
            IsBusy = false;
            BusyMessage = "Ready";
            StatusText = "Ready";
        }
    }

    private async Task RefreshDashboardAsync()
    {
        await RunBusyAsync("Refreshing device diagnostics", async () =>
        {
            ApplyDashboard(await _service.GetDashboardAsync(CreateCallbacks()));
            await ShowToastAsync("Diagnostics refreshed", "The latest local device health signals are now displayed.", "success");
        });
    }

    private void ApplyDashboard(DashboardSnapshot snapshot)
    {
        var device = OverviewCards[0];
        device.PrimaryText = $"{snapshot.Device.Manufacturer} {snapshot.Device.Model}".Trim();
        device.SecondaryText = $"CPU: {snapshot.Device.ProcessorName}\nRAM: {FormatNullable(snapshot.Device.InstalledMemoryGb, "0.0", "Unknown")} GB";
        device.Status = "Healthy";

        var os = OverviewCards[1];
        var display = string.Join(" ", new[] { snapshot.OperatingSystem.Caption, snapshot.OperatingSystem.DisplayVersion }.Where(value => !string.IsNullOrWhiteSpace(value)));
        os.PrimaryText = display;
        os.SecondaryText = $"Build {snapshot.OperatingSystem.BuildNumber}\nInstalled {FormatDate(snapshot.OperatingSystem.InstalledAt)} - uptime {FormatUptime(snapshot.OperatingSystem.Uptime)}";
        os.Status = "Healthy";

        var battery = OverviewCards[2];
        battery.PrimaryText = snapshot.Battery.IsPresent ? snapshot.Battery.Health.Headline : "No battery detected";
        battery.SecondaryText = snapshot.Battery.IsPresent
            ? $"Wear {FormatNullable(snapshot.Battery.WearPercent, "0.0", "Unknown")}% - {snapshot.Battery.CycleCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} cycles\n{snapshot.Battery.Health.Detail}"
            : "Battery telemetry is not exposed on this device.";
        battery.Status = snapshot.Battery.Health.State.ToString();

        var storage = OverviewCards[3];
        storage.PrimaryText = snapshot.Storage.UsedPercent is null
            ? $"{snapshot.Storage.Drive} status unavailable"
            : $"{snapshot.Storage.UsedPercent:0}% used on {snapshot.Storage.Drive}";
        storage.SecondaryText = $"{FormatNullable(snapshot.Storage.FreeGb, "0.0", "Unknown")} GB free of {FormatNullable(snapshot.Storage.TotalGb, "0.0", "Unknown")} GB\n{snapshot.Storage.Health.Detail}";
        storage.Status = snapshot.Storage.Health.State.ToString();

        var disks = OverviewCards[4];
        disks.PrimaryText = snapshot.Disks.Count == 0 ? "No physical disk telemetry" : $"{snapshot.Disks.Count} physical disk{(snapshot.Disks.Count == 1 ? string.Empty : "s")} detected";
        disks.SecondaryText = snapshot.Disks.Count == 0
            ? "Windows did not return physical disk health data."
            : string.Join("\n", snapshot.Disks.Take(3).Select(disk => $"{disk.Name} - {disk.Health.Headline}"));
        disks.Status = Worst(snapshot.Disks.Select(disk => disk.Health).ToArray()).State.ToString();

        var thermal = OverviewCards[5];
        thermal.PrimaryText = snapshot.Thermal.Health.Headline;
        var temperatureText = snapshot.Thermal.CurrentZoneTemperaturesCelsius.Count == 0
            ? "Thermal sensors are not exposed by firmware."
            : "Current zones: " + string.Join(", ", snapshot.Thermal.CurrentZoneTemperaturesCelsius.Select(value => $"{value:0.#} C"));
        thermal.SecondaryText = $"{snapshot.Thermal.ThrottleEngagementCount} throttle events - {snapshot.Thermal.FirmwareSpeedCapCount} firmware caps - {snapshot.Thermal.HardwareErrorCount} hardware errors\n{temperatureText}";
        thermal.Status = snapshot.Thermal.Health.State.ToString();

        var assessments = new List<HealthAssessment> { snapshot.Battery.Health, snapshot.Storage.Health, snapshot.Thermal.Health };
        assessments.AddRange(snapshot.Disks.Select(disk => disk.Health));
        var overall = Worst(assessments.ToArray());
        HealthTitle = "System health";
        HealthMessage = overall.State <= HealthState.Healthy ? "All critical systems are normal." : "One or more signals need review.";
        HealthDetail = overall.State <= HealthState.Healthy ? "No urgent action required" : overall.Headline;

        var concerns = assessments.Where(assessment => assessment.State is HealthState.Advisory or HealthState.Warning or HealthState.Critical).ToList();
        AttentionMessage = concerns.Count == 0 ? "No active health warnings were found." : "Some items may impact reliability or performance.";
        AttentionDetail = concerns.Count == 0 ? "0 items need your attention" : $"{concerns.Count} item{(concerns.Count == 1 ? string.Empty : "s")} need your attention";
        var concern = concerns.OrderByDescending(assessment => assessment.State).FirstOrDefault();
        ConcernTitle = concern?.Headline ?? "No urgent recommendations";
        ConcernMessage = concern?.Detail ?? "Continue routine maintenance and monitor thermal history.";
    }

    private static HealthAssessment Worst(params HealthAssessment[] assessments) => assessments
        .Where(assessment => assessment is not null)
        .OrderByDescending(assessment => assessment.State)
        .FirstOrDefault() ?? new(HealthState.Unavailable, "Status unavailable", "No health data was returned.");

    private async Task RefreshPowerAsync()
    {
        await RunBusyAsync("Refreshing power status", async () =>
        {
            ApplyPowerStatus(await _service.GetPowerStatusAsync(CreateCallbacks()));
            await ShowToastAsync("Power status refreshed", "Processor and Windows power settings are current.", "success");
        });
    }

    private void ApplyPowerStatus(PowerStatus status)
    {
        CpuAcPercent = status.AcMaximumProcessorPercent ?? 0;
        CpuDcPercent = status.DcMaximumProcessorPercent ?? 0;
        CpuAcLabel = status.AcMaximumProcessorPercent is null ? "Unavailable" : $"{status.AcMaximumProcessorPercent}%";
        CpuDcLabel = status.DcMaximumProcessorPercent is null ? "Unavailable" : $"{status.DcMaximumProcessorPercent}%";
        CpuModeLabel = status.TurboBoost switch
        {
            TurboBoostState.Enabled => "Turbo enabled",
            TurboBoostState.Disabled => "Cool mode",
            TurboBoostState.PartiallyCapped => "Mixed limits",
            _ => "Unavailable"
        };
        CpuStatusText = status.TurboBoost switch
        {
            TurboBoostState.Enabled => "Turbo Boost is enabled. The processor can use its full peak clock range.",
            TurboBoostState.Disabled => "Turbo Boost is disabled on AC and battery power for lower sustained temperatures.",
            TurboBoostState.PartiallyCapped => "AC and battery processor ceilings differ. Review both limits before changing modes.",
            _ => "Windows did not return the current processor ceiling."
        };
        PowerModeText = status.ActiveOverlayName;
        ActivePowerPlan = string.IsNullOrWhiteSpace(status.ActivePlanGuid)
            ? status.ActivePlanName
            : $"{status.ActivePlanName}\n{status.ActivePlanGuid}";
    }

    private async Task SetCpuCapAsync(object? parameter)
    {
        if (!int.TryParse(parameter?.ToString(), out var percent))
        {
            return;
        }

        await RunBusyAsync($"Setting processor ceiling to {percent}%", async () =>
        {
            ApplyPowerStatus(await _service.SetCpuMaximumAsync(percent, CreateCallbacks()));
            var detail = percent < 100 ? "Turbo Boost is now disabled for cooler sustained operation." : "Full processor performance has been restored.";
            await ShowToastAsync("Processor ceiling updated", detail, "success");
        });
    }

    private async Task SetPowerModeAsync(object? parameter)
    {
        if (!Enum.TryParse<PowerOverlay>(parameter?.ToString(), true, out var overlay))
        {
            return;
        }

        await RunBusyAsync("Updating Windows power mode", async () =>
        {
            ApplyPowerStatus(await _service.SetPowerOverlayAsync(overlay, CreateCallbacks()));
            await ShowToastAsync("Power mode updated", "Windows is now using the selected operating profile.", "success");
        });
    }

    private async Task GenerateBatteryReportAsync()
    {
        await RunBusyAsync("Generating battery report", async () =>
        {
            var path = await _service.GenerateBatteryReportAsync(callbacks: CreateCallbacks());
            OpenPath(path);
            await ShowToastAsync("Battery report created", "The Windows battery report was saved and opened.", "success");
        });
    }

    private void SelectThermalRange(object? parameter)
    {
        if (int.TryParse(parameter?.ToString(), out var days) && days is 7 or 30 or 90)
        {
            _thermalDays = days;
            ThermalSummaryTitle = $"Ready to scan the last {days} days";
        }
    }

    private async Task ScanThermalAsync()
    {
        IsThermalLoading = true;
        try
        {
            await RunBusyAsync($"Scanning {_thermalDays} days of thermal history", async () =>
            {
                var events = await _service.ScanThermalEventsAsync(_thermalDays, CreateCallbacks());
                ThermalEvents.Clear();
                foreach (var item in events)
                {
                    ThermalEvents.Add(item);
                }

                OnPropertyChanged(nameof(ThermalEventCountText));
                RaiseCommandStates();
                ThermalSummaryTitle = events.Count == 0 ? "No thermal incidents found" : $"{events.Count} thermal and hardware event{(events.Count == 1 ? string.Empty : "s")} found";
                ThermalSummary = events.Count == 0
                    ? $"The System log contains no matching throttle, firmware cap, WHEA, or thermal warning events in the last {_thermalDays} days."
                    : $"Review the newest events first. Critical and warning entries may indicate a repeatable heat or hardware pattern.";
                await ShowToastAsync(events.Count == 0 ? "Thermal history is clear" : "Thermal scan complete", events.Count == 0 ? "No matching events were recorded." : $"{events.Count} events are ready to review.", events.Count == 0 ? "success" : "info");
            });
        }
        finally
        {
            IsThermalLoading = false;
        }
    }

    private async Task ExportThermalAsync()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, $"SurfaceMedic-events-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var lines = new List<string> { "Time,Level,Provider,Id,Kind,Message" };
        lines.AddRange(ThermalEvents.Select(item => string.Join(",", new[]
        {
            Csv(item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            Csv(item.Level),
            Csv(item.Provider),
            item.EventId.ToString(CultureInfo.InvariantCulture),
            Csv(item.Kind.ToString()),
            Csv(item.Message)
        })));
        await File.WriteAllLinesAsync(path, lines, new UTF8Encoding(true));
        AppendLog(LogLevel.Information, "Export thermal events", $"Saved {ThermalEvents.Count} events to {path}.");
        await ShowToastAsync("Thermal events exported", $"Saved {ThermalEvents.Count} rows to your Desktop.", "success");
    }

    private async Task SearchPackagesAsync()
    {
        var query = SearchQuery.Trim();
        await RunBusyAsync($"Searching winget for {query}", async () =>
        {
            var packages = await _service.SearchPackagesAsync(query, CreateCallbacks());
            Replace(Packages, packages);
            SelectedPackage = null;
            await ShowToastAsync("Package search complete", packages.Count == 0 ? "No matching packages were found." : $"{packages.Count} package results are ready.", packages.Count == 0 ? "info" : "success");
        });
    }

    private async Task InstallSelectedPackageAsync()
    {
        if (SelectedPackage is null)
        {
            return;
        }

        await InstallPackagesAsync([SelectedPackage.Id], SelectedPackage.Name);
    }

    private async Task InstallRecommendedToolAsync(object? parameter)
    {
        var id = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        await InstallPackagesAsync([id], id);
    }

    private async Task InstallPackagesAsync(IEnumerable<string> ids, string label)
    {
        await RunBusyAsync($"Installing {label}", async () =>
        {
            var result = await _service.InstallPackagesAsync(ids, CreateCallbacks());
            await ReportOperationResultAsync(result, "Installation complete", "Installation needs attention");
        });
    }

    private async Task CheckUpdatesAsync()
    {
        await RunBusyAsync("Checking package updates", async () =>
        {
            var updates = await _service.GetPackageUpdatesAsync(CreateCallbacks());
            Replace(PackageUpdates, updates);
            SelectedPackageUpdate = null;
            await ShowToastAsync(updates.Count == 0 ? "Software is up to date" : "Updates are available", updates.Count == 0 ? "winget found no package upgrades." : $"{updates.Count} package updates are ready to review.", updates.Count == 0 ? "success" : "info");
        });
    }

    private async Task UpgradeSelectedPackageAsync()
    {
        if (SelectedPackageUpdate is null)
        {
            return;
        }

        await RunBusyAsync($"Upgrading {SelectedPackageUpdate.Name}", async () =>
        {
            var result = await _service.UpgradePackagesAsync([SelectedPackageUpdate.Id], CreateCallbacks());
            await ReportOperationResultAsync(result, "Package upgraded", "Upgrade needs attention");
            if (result.Succeeded)
            {
                PackageUpdates.Remove(SelectedPackageUpdate);
                SelectedPackageUpdate = null;
            }
        });
    }

    private async Task UpgradeAllPackagesAsync()
    {
        await RunBusyAsync("Upgrading all available packages", async () =>
        {
            var result = await _service.UpgradeAllPackagesAsync(CreateCallbacks());
            await ReportOperationResultAsync(result, "Packages upgraded", "Some upgrades need attention");
            if (result.Succeeded)
            {
                PackageUpdates.Clear();
            }
        });
    }

    private async Task RunMaintenanceAsync(object? parameter)
    {
        if (!Enum.TryParse<MaintenanceOperation>(parameter?.ToString(), true, out var operation))
        {
            return;
        }

        var label = Humanize(operation.ToString());
        await RunBusyAsync($"Running {label.ToLowerInvariant()}", async () =>
        {
            var result = await _service.RunMaintenanceAsync(operation, CreateCallbacks());
            await ReportOperationResultAsync(result, $"{label} complete", $"{label} needs attention");
        });
    }

    private async Task ReportOperationResultAsync(OperationResult result, string successTitle, string failureTitle)
    {
        await ShowToastAsync(result.Succeeded ? successTitle : failureTitle, result.Summary, result.Succeeded ? "success" : "error");
    }

    private void Navigate(object? target)
    {
        if (target is not AppPage page && !Enum.TryParse(target?.ToString(), true, out page))
        {
            return;
        }

        var navigation = PrimaryNavigation.Concat(SecondaryNavigation).First(item => item.Page == page);
        SelectedNavigation = navigation;
    }

    private void ApplyTheme(string theme)
    {
        if (_settings.Theme.Equals(theme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.Theme = theme;
        _settingsService.Save(_settings);
        ThemeManager.Apply(theme);
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        _ = ShowToastAsync($"{theme} theme applied", "All interface surfaces and status colors have been updated.", "success");
    }

    private void CopyLog()
    {
        if (string.IsNullOrWhiteSpace(LogText))
        {
            return;
        }

        try
        {
            Clipboard.SetText(LogText);
            _ = ShowToastAsync("Activity log copied", "The current session log is on the clipboard.", "success");
        }
        catch (Exception exception)
        {
            HandleCommandError(exception);
        }
    }

    private void ClearLog()
    {
        LogText = string.Empty;
        AppendLog(LogLevel.Information, "Activity log", "The visible activity log was cleared.");
    }

    private void AppendLog(LogLevel level, string operation, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{LevelLabel(level)}] {operation}: {message}";
        LogText = string.IsNullOrEmpty(LogText) ? line : LogText + Environment.NewLine + line;
        if (LogText.Length > 400_000)
        {
            LogText = LogText[^200_000..];
        }

        try
        {
            lock (_logLock)
            {
                File.AppendAllText(_sessionLogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never interrupt a repair operation.
        }
    }

    private async Task ShowToastAsync(string title, string message, string kind)
    {
        var sequence = Interlocked.Increment(ref _toastSequence);
        ToastTitle = title;
        ToastMessage = message;
        ToastGlyph = kind switch { "error" => "\uEA39", "info" => "\uE946", _ => "\uE73E" };
        IsToastVisible = true;
        await Task.Delay(3600);
        if (sequence == _toastSequence)
        {
            IsToastVisible = false;
        }
    }

    private void HandleCommandError(Exception exception)
    {
        AppendLog(LogLevel.Error, "Operation", exception.Message);
        _ = ShowToastAsync("Operation could not be completed", exception.Message, "error");
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static string FormatNullable(double? value, string format, string fallback) =>
        value?.ToString(format, CultureInfo.InvariantCulture) ?? fallback;

    private static string FormatDate(DateTimeOffset? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown";

    private static string FormatUptime(TimeSpan? value) => value is null ? "unknown" : $"{(int)value.Value.TotalDays}d {value.Value.Hours}h {value.Value.Minutes}m";

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static string Humanize(string value)
    {
        var result = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]))
            {
                result.Append(' ');
            }

            result.Append(value[index]);
        }

        return result.ToString();
    }

    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Command => "CMD",
        LogLevel.Output => "OUT",
        _ => "DEBUG"
    };

    private static void OpenUri(string uri, string logMessage)
    {
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    private static void OpenPath(string path)
    {
        if (Directory.Exists(path) || File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }
}
