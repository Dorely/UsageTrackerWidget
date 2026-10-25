using System.IO;

namespace UsageTrackerWidget.App.Storage;

internal static class AppPaths
{
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UsageTrackerWidget");

    public static string SettingsFile { get; } = Path.Combine(RootDirectory, "settings.json");

    public static string HistoryFile { get; } = Path.Combine(RootDirectory, "usage.db");

    public static void EnsureRootDirectory()
    {
        Directory.CreateDirectory(RootDirectory);
    }
}
