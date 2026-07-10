namespace SurfaceMedic.Core.Models;

public enum PowerOverlay
{
    Balanced,
    BestPowerEfficiency,
    BetterPerformance,
    BestPerformance
}

public enum TurboBoostState
{
    Unavailable,
    Enabled,
    Disabled,
    PartiallyCapped
}

public sealed record PowerStatus
{
    public DateTimeOffset CollectedAt { get; init; }
    public int? AcMaximumProcessorPercent { get; init; }
    public int? DcMaximumProcessorPercent { get; init; }
    public TurboBoostState TurboBoost { get; init; }
    public PowerOverlay ActiveOverlay { get; init; }
    public string ActiveOverlayName { get; init; } = "Balanced";
    public string? ActiveOverlayGuid { get; init; }
    public string ActivePlanName { get; init; } = "Unavailable";
    public string? ActivePlanGuid { get; init; }
    public HealthAssessment Health { get; init; } = HealthAssessor.Unavailable("Power status unavailable");
}
