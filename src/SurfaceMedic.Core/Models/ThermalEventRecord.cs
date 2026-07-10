namespace SurfaceMedic.Core.Models;

public enum ThermalEventKind
{
    ThermalThrottle,
    FirmwareSpeedCap,
    HardwareError,
    ThermalWarning
}

public sealed record ThermalEventRecord
{
    public DateTimeOffset Timestamp { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public int EventId { get; init; }
    public long? RecordId { get; init; }
    public string Message { get; init; } = string.Empty;
    public ThermalEventKind Kind { get; init; }
}
