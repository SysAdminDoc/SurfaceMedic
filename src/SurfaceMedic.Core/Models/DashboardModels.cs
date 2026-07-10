namespace SurfaceMedic.Core.Models;

public sealed record DashboardSnapshot
{
    public DateTimeOffset CollectedAt { get; init; }
    public DeviceInfo Device { get; init; } = new();
    public OperatingSystemInfo OperatingSystem { get; init; } = new();
    public BatteryInfo Battery { get; init; } = new();
    public StorageInfo Storage { get; init; } = new();
    public IReadOnlyList<PhysicalDiskInfo> Disks { get; init; } = [];
    public ThermalSummary Thermal { get; init; } = new();
}

public sealed record DeviceInfo
{
    public string Manufacturer { get; init; } = "Unavailable";
    public string Model { get; init; } = "Unavailable";
    public string ProcessorName { get; init; } = "Unavailable";
    public double? InstalledMemoryGb { get; init; }
}

public sealed record OperatingSystemInfo
{
    public string Caption { get; init; } = "Unavailable";
    public string DisplayVersion { get; init; } = string.Empty;
    public string BuildNumber { get; init; } = string.Empty;
    public DateTimeOffset? InstalledAt { get; init; }
    public TimeSpan? Uptime { get; init; }
}

public sealed record BatteryInfo
{
    public bool IsPresent { get; init; }
    public double? DesignCapacityMwh { get; init; }
    public double? FullChargeCapacityMwh { get; init; }
    public double? WearPercent { get; init; }
    public int? CycleCount { get; init; }
    public HealthAssessment Health { get; init; } = HealthAssessor.Unavailable("Battery status unavailable");
}

public sealed record StorageInfo
{
    public string Drive { get; init; } = "C:";
    public double? TotalGb { get; init; }
    public double? FreeGb { get; init; }
    public double? UsedPercent { get; init; }
    public HealthAssessment Health { get; init; } = HealthAssessor.Unavailable("Storage status unavailable");
}

public sealed record PhysicalDiskInfo
{
    public string Name { get; init; } = "Unknown disk";
    public string MediaType { get; init; } = "Unspecified";
    public string ReportedHealth { get; init; } = "Unknown";
    public double? WearPercent { get; init; }
    public double? TemperatureCelsius { get; init; }
    public HealthAssessment Health { get; init; } = HealthAssessor.Unavailable("Disk status unavailable");
}

public sealed record ThermalSummary
{
    public int ThrottleEngagementCount { get; init; }
    public int FirmwareSpeedCapCount { get; init; }
    public int HardwareErrorCount { get; init; }
    public IReadOnlyList<double> CurrentZoneTemperaturesCelsius { get; init; } = [];
    public HealthAssessment Health { get; init; } = HealthAssessor.Unavailable("Thermal status unavailable");
}
