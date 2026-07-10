namespace SurfaceMedic.Core.Models;

public sealed record PackageRecord
{
    public string Name { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Available { get; init; } = string.Empty;
    public string Match { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}
