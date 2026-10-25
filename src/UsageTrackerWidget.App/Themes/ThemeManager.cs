using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace UsageTrackerWidget.App.Themes;

internal sealed class ThemeManager : IDisposable
{
    private bool _disposed;

    public ThemeManager()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void Apply()
    {
        var palette = SystemParameters.HighContrast
            ? CreateHighContrastPalette()
            : IsLightTheme()
                ? CreateLightPalette()
                : CreateDarkPalette();

        var resources = Application.Current.Resources;
        foreach (var entry in palette)
        {
            resources[entry.Key] = entry.Value;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs eventArgs)
    {
        if (eventArgs.Category is UserPreferenceCategory.General
            or UserPreferenceCategory.VisualStyle
            or UserPreferenceCategory.Color)
        {
            Application.Current.Dispatcher.BeginInvoke(Apply);
        }
    }

    private static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Dictionary<string, Brush> CreateDarkPalette()
    {
        return Palette(
            background: "#0B0F15",
            surface: "#151A22",
            surfaceRaised: "#1C232E",
            surfaceHover: "#263140",
            border: "#2A3442",
            borderStrong: "#3A4656",
            textPrimary: "#F1F5F9",
            textSecondary: "#9AA8BA",
            textMuted: "#738195",
            accent: "#5B9DFF",
            accentHover: "#7BAFFF",
            accentSoft: "#1A2A42",
            textOnAccent: "#07111F",
            success: "#4ADE80",
            warning: "#FBBF24",
            danger: "#FB7185",
            track: "#293341",
            dangerSoft: "#3A1D25",
            warningSoft: "#362A13",
            successSoft: "#153123",
            inputBackground: "#10151D",
            divider: "#222C39");
    }

    private static Dictionary<string, Brush> CreateLightPalette()
    {
        return Palette(
            background: "#EDF2F8",
            surface: "#FAFBFD",
            surfaceRaised: "#FFFFFF",
            surfaceHover: "#EFF3F8",
            border: "#D9E0EA",
            borderStrong: "#C2CCD9",
            textPrimary: "#18212D",
            textSecondary: "#566476",
            textMuted: "#778598",
            accent: "#2563EB",
            accentHover: "#1D4ED8",
            accentSoft: "#E6EFFF",
            textOnAccent: "#FFFFFF",
            success: "#15803D",
            warning: "#A85A00",
            danger: "#BE123C",
            track: "#E4EAF2",
            dangerSoft: "#FDE8EE",
            warningSoft: "#FFF4DB",
            successSoft: "#E5F7EC",
            inputBackground: "#F2F5F9",
            divider: "#E3E8F0");
    }

    private static Dictionary<string, Brush> CreateHighContrastPalette()
    {
        return new Dictionary<string, Brush>(StringComparer.Ordinal)
        {
            ["AppBackgroundBrush"] = SystemColors.WindowBrush,
            ["SurfaceBrush"] = SystemColors.WindowBrush,
            ["SurfaceRaisedBrush"] = SystemColors.WindowBrush,
            ["SurfaceHoverBrush"] = SystemColors.ControlBrush,
            ["BorderBrush"] = SystemColors.WindowFrameBrush,
            ["BorderStrongBrush"] = SystemColors.WindowFrameBrush,
            ["TextPrimaryBrush"] = SystemColors.WindowTextBrush,
            ["TextSecondaryBrush"] = SystemColors.WindowTextBrush,
            ["TextMutedBrush"] = SystemColors.GrayTextBrush,
            ["AccentBrush"] = SystemColors.HighlightBrush,
            ["AccentHoverBrush"] = SystemColors.HighlightBrush,
            ["AccentSoftBrush"] = SystemColors.WindowBrush,
            ["TextOnAccentBrush"] = SystemColors.HighlightTextBrush,
            ["SuccessBrush"] = SystemColors.WindowTextBrush,
            ["WarningBrush"] = SystemColors.WindowTextBrush,
            ["DangerBrush"] = SystemColors.WindowTextBrush,
            ["TrackBrush"] = SystemColors.ControlBrush,
            ["DangerSoftBrush"] = SystemColors.WindowBrush,
            ["WarningSoftBrush"] = SystemColors.WindowBrush,
            ["SuccessSoftBrush"] = SystemColors.WindowBrush,
            ["InputBackgroundBrush"] = SystemColors.WindowBrush,
            ["DividerBrush"] = SystemColors.WindowFrameBrush,
        };
    }

    private static Dictionary<string, Brush> Palette(
        string background,
        string surface,
        string surfaceRaised,
        string surfaceHover,
        string border,
        string borderStrong,
        string textPrimary,
        string textSecondary,
        string textMuted,
        string accent,
        string accentHover,
        string accentSoft,
        string textOnAccent,
        string success,
        string warning,
        string danger,
        string track,
        string dangerSoft,
        string warningSoft,
        string successSoft,
        string inputBackground,
        string divider)
    {
        return new Dictionary<string, Brush>(StringComparer.Ordinal)
        {
            ["AppBackgroundBrush"] = BrushFrom(background),
            ["SurfaceBrush"] = BrushFrom(surface),
            ["SurfaceRaisedBrush"] = BrushFrom(surfaceRaised),
            ["SurfaceHoverBrush"] = BrushFrom(surfaceHover),
            ["BorderBrush"] = BrushFrom(border),
            ["BorderStrongBrush"] = BrushFrom(borderStrong),
            ["TextPrimaryBrush"] = BrushFrom(textPrimary),
            ["TextSecondaryBrush"] = BrushFrom(textSecondary),
            ["TextMutedBrush"] = BrushFrom(textMuted),
            ["AccentBrush"] = BrushFrom(accent),
            ["AccentHoverBrush"] = BrushFrom(accentHover),
            ["AccentSoftBrush"] = BrushFrom(accentSoft),
            ["TextOnAccentBrush"] = BrushFrom(textOnAccent),
            ["SuccessBrush"] = BrushFrom(success),
            ["WarningBrush"] = BrushFrom(warning),
            ["DangerBrush"] = BrushFrom(danger),
            ["TrackBrush"] = BrushFrom(track),
            ["DangerSoftBrush"] = BrushFrom(dangerSoft),
            ["WarningSoftBrush"] = BrushFrom(warningSoft),
            ["SuccessSoftBrush"] = BrushFrom(successSoft),
            ["InputBackgroundBrush"] = BrushFrom(inputBackground),
            ["DividerBrush"] = BrushFrom(divider),
        };
    }

    private static SolidColorBrush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
