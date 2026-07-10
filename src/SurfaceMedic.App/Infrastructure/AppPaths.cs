using System.IO;

namespace SurfaceMedic.App.Infrastructure;

public static class AppPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SurfaceMedic");

    public static string LogsDirectory { get; } = Path.Combine(AppDataDirectory, "logs");

    public static string SettingsFile { get; } = Path.Combine(AppDataDirectory, "settings.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
