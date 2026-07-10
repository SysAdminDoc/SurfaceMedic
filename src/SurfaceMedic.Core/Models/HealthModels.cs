namespace SurfaceMedic.Core.Models;

public enum HealthState
{
    Unavailable,
    Healthy,
    Advisory,
    Warning,
    Critical
}

public sealed record HealthAssessment(
    HealthState State,
    string Headline,
    string Detail);
