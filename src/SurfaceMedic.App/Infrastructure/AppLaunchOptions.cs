namespace SurfaceMedic.App.Infrastructure;

public sealed record AppLaunchOptions(bool Smoke, bool Background, bool SkipElevation)
{
    public static AppLaunchOptions Parse(IEnumerable<string> arguments)
    {
        var values = new HashSet<string>(arguments, StringComparer.OrdinalIgnoreCase);
        var smoke = values.Contains("--smoke");
        var background = smoke || values.Contains("--uia-background");
        var skipElevation = background || values.Contains("--no-elevate");
        return new(smoke, background, skipElevation);
    }
}
