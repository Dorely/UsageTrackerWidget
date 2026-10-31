using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UsageTrackerWidget.App.Storage;

namespace UsageTrackerWidget.App.ViewModels;

internal sealed partial class WindowAutoStartViewModel : ObservableObject
{
    private readonly Action<WindowAutoStartViewModel> _changed;
    private WindowAutoStartSettings _settings;
    private bool _suppressChanged;

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private string timeText = string.Empty;

    [ObservableProperty]
    private string timeError = string.Empty;

    [ObservableProperty]
    private string outcome = string.Empty;

    [ObservableProperty]
    private bool outcomeIsError;

    [ObservableProperty]
    private string blockedReason = string.Empty;

    public WindowAutoStartViewModel(
        string windowKey,
        string title,
        WindowAutoStartSettings settings,
        Action<WindowAutoStartViewModel> changed)
    {
        WindowKey = windowKey;
        Title = title;
        _settings = settings;
        _changed = changed;

        _suppressChanged = true;
        IsEnabled = settings.Enabled;
        TimeText = FormatTime(settings.Time);
        _suppressChanged = false;
    }

    public string WindowKey { get; }

    public string Title { get; }

    public WindowAutoStartSettings Settings => _settings;

    public bool IsTimeMode => !_settings.IsRolling;

    public bool IsRollingMode => _settings.IsRolling;

    public bool HasTimeError => TimeError.Length > 0;

    public bool HasOutcome => Outcome.Length > 0;

    public bool IsBlocked => BlockedReason.Length > 0;

    public string ScheduleText => _settings.IsRolling
        ? "Starts again whenever the window is idle."
        : $"Starts daily at {FormatTime(_settings.Time)} if the window is idle.";

    public bool IsCardVisible => _settings.Enabled;

    public string CardText
    {
        get
        {
            if (!_settings.Enabled)
            {
                return string.Empty;
            }

            if (IsBlocked)
            {
                return "Auto-start paused · image tracking is off";
            }

            var schedule = _settings.IsRolling
                ? "Auto-start when idle"
                : $"Auto-start {FormatTime(_settings.Time)}";
            return HasOutcome ? $"{schedule} · {Outcome}" : schedule;
        }
    }

    [RelayCommand]
    private void SelectTimeMode()
    {
        Update(_settings with { Mode = WindowAutoStartSettings.TimeMode });
    }

    [RelayCommand]
    private void SelectRollingMode()
    {
        Update(_settings with { Mode = WindowAutoStartSettings.RollingMode });
    }

    public void SetOutcome(string? outcome, bool isError)
    {
        Outcome = outcome ?? string.Empty;
        OutcomeIsError = isError;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressChanged)
        {
            return;
        }

        Update(_settings with { Enabled = value });
    }

    partial void OnTimeTextChanged(string value)
    {
        if (_suppressChanged)
        {
            return;
        }

        if (!TryParseTime(value, out var time))
        {
            TimeError = "Enter a time such as 5:00 AM or 17:30.";
            return;
        }

        TimeError = string.Empty;
        _suppressChanged = true;
        TimeText = FormatTime(time);
        _suppressChanged = false;
        Update(_settings with { LocalTime = time.ToString("HH:mm", CultureInfo.InvariantCulture) });
    }

    partial void OnTimeErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasTimeError));
    }

    partial void OnOutcomeChanged(string value)
    {
        OnPropertyChanged(nameof(HasOutcome));
        OnPropertyChanged(nameof(CardText));
    }

    partial void OnBlockedReasonChanged(string value)
    {
        OnPropertyChanged(nameof(IsBlocked));
        OnPropertyChanged(nameof(CardText));
    }

    private void Update(WindowAutoStartSettings settings)
    {
        if (settings == _settings)
        {
            return;
        }

        _settings = settings;
        Outcome = string.Empty;
        OutcomeIsError = false;
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(IsTimeMode));
        OnPropertyChanged(nameof(IsRollingMode));
        OnPropertyChanged(nameof(ScheduleText));
        OnPropertyChanged(nameof(CardText));
        OnPropertyChanged(nameof(IsCardVisible));
        _changed(this);
    }

    private static bool TryParseTime(string text, out TimeOnly time)
    {
        var trimmed = text.Trim();
        return TimeOnly.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.None, out time) ||
            TimeOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    private static string FormatTime(TimeOnly time)
    {
        return time.ToString("t", CultureInfo.CurrentCulture);
    }
}
