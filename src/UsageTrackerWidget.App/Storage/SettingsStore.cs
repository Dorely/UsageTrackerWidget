using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsageTrackerWidget.App.Storage;

internal sealed record AppSettings(
    bool StartWithWindows = true,
    bool TrackImageAllowance = true,
    string TrayIconMetric = "highest",
    WindowAutoStartSettings? SessionAutoStart = null,
    WindowAutoStartSettings? ImageAutoStart = null)
{
    [JsonIgnore]
    public WindowAutoStartSettings SessionAutoStartOrDefault =>
        SessionAutoStart ?? WindowAutoStartSettings.DefaultSession;

    [JsonIgnore]
    public WindowAutoStartSettings ImageAutoStartOrDefault =>
        ImageAutoStart ?? WindowAutoStartSettings.DefaultImages;
}

// Mode is "time" (once a day at LocalTime, HH:mm) or "rolling" (whenever the window is idle).
internal sealed record WindowAutoStartSettings(
    bool Enabled = false,
    string Mode = WindowAutoStartSettings.TimeMode,
    string LocalTime = "05:00")
{
    public const string TimeMode = "time";
    public const string RollingMode = "rolling";

    public static WindowAutoStartSettings DefaultSession { get; } = new();

    public static WindowAutoStartSettings DefaultImages { get; } = new(Mode: RollingMode);

    [JsonIgnore]
    public bool IsRolling => Mode == RollingMode;

    [JsonIgnore]
    public TimeOnly Time => TimeOnly.TryParseExact(
        LocalTime,
        "HH:mm",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out var parsed)
        ? parsed
        : new TimeOnly(5, 0);

    public WindowAutoStartSettings Normalize(WindowAutoStartSettings fallback)
    {
        return this with
        {
            Mode = Mode is TimeMode or RollingMode ? Mode : fallback.Mode,
            LocalTime = Time.ToString("HH:mm", CultureInfo.InvariantCulture),
        };
    }
}

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? AppPaths.SettingsFile;
    }

    public bool Exists => File.Exists(_path);

    public AppSettings Load()
    {
        try
        {
            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            return settings with
            {
                TrayIconMetric = string.IsNullOrWhiteSpace(settings.TrayIconMetric)
                    ? "highest"
                    : settings.TrayIconMetric,
                SessionAutoStart = settings.SessionAutoStartOrDefault.Normalize(WindowAutoStartSettings.DefaultSession),
                ImageAutoStart = settings.ImageAutoStartOrDefault.Normalize(WindowAutoStartSettings.DefaultImages),
            };
        }
        catch (FileNotFoundException)
        {
            return new AppSettings();
        }
        catch (DirectoryNotFoundException)
        {
            return new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        AppPaths.EnsureRootDirectory();
        var temporaryPath = $"{_path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
