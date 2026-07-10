namespace SurfaceMedic.Core.Models;

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error,
    Command,
    Output
}

public enum MaintenanceOperation
{
    CleanTemporaryFiles,
    SystemFileCheck,
    ScanComponentStore,
    RepairComponentStore,
    CleanupComponentStore,
    FlushDns
}

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Operation,
    string Message);

public sealed record OperationProgress(
    string Operation,
    string Message,
    int? Percent = null,
    bool IsIndeterminate = true);

public sealed record OperationCallbacks(
    IProgress<OperationProgress>? Progress = null,
    IProgress<LogEntry>? Log = null);

public sealed record OperationResult
{
    public string Operation { get; init; } = string.Empty;
    public bool Succeeded { get; init; }
    public int ExitCode { get; init; }
    public string Summary { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
}
