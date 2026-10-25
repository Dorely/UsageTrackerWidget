using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using UsageTrackerWidget.App.Storage;
using UsageTrackerWidget.Core;

namespace UsageTrackerWidget.App.ViewModels;

internal enum UsageWindowState
{
    Unknown,
    Current,
    Stale,
    TrackingOff,
}

internal sealed class UsageWindowViewModel : ObservableObject
{
    private double? _usedPercent;
    private TimeSpan? _windowLength;
    private DateTimeOffset? _resetsAt;
    private bool _isLimitReached;
    private UsageWindowState _state = UsageWindowState.Unknown;
    private IReadOnlyList<UsageHistoryPoint> _history = [];
    private string _historyRangeLabel = string.Empty;

    public UsageWindowViewModel(string key, string displayName, string shortName)
    {
        Key = key;
        DisplayName = displayName;
        ShortName = shortName;
    }

    public string Key { get; }

    public string DisplayName { get; }

    public string ShortName { get; }

    public double? UsedPercent => _usedPercent;

    public IReadOnlyList<UsageHistoryPoint> History => _history;

    public string HistoryRangeLabel => _historyRangeLabel;

    public bool IsKnown => _usedPercent is not null;

    public bool IsStale => _state == UsageWindowState.Stale;

    public bool IsTrackingOff => _state == UsageWindowState.TrackingOff;

    public bool IsAtLimit => _isLimitReached || _usedPercent >= 100;

    public bool IsHighUsage => _usedPercent >= 70;

    public bool IsCriticalUsage => _usedPercent >= 90;

    public bool HasHistory => _history.Count >= 2;

    public bool ShowStatusBadge => _state is not UsageWindowState.Current || IsAtLimit;

    public string StateKind => _state switch
    {
        UsageWindowState.Current when IsAtLimit => "limit",
        UsageWindowState.Current => "current",
        UsageWindowState.Stale => "stale",
        UsageWindowState.TrackingOff => "off",
        _ => "unknown",
    };

    public string StatusLabel => _state switch
    {
        UsageWindowState.Current when IsAtLimit => "Limit reached",
        UsageWindowState.Current => "Live",
        UsageWindowState.Stale => "Stale",
        UsageWindowState.TrackingOff => "Tracking off",
        _ => "Unknown",
    };

    public string UsedPercentText => _usedPercent is { } used
        ? $"{FormatPercent(used)}%"
        : "—";

    public string RemainingText => _usedPercent is { } used
        ? $"{FormatPercent(Math.Max(0, 100 - used))}% left"
        : "Remaining unknown";

    public double ProgressValue => _usedPercent ?? 0;

    public string ResetText => FormatResetText(_resetsAt, DateTimeOffset.Now);

    public string WindowLengthText => FormatWindowLength(_windowLength);

    public string ToolTipLine => _usedPercent is { } used
        ? $"{ShortName} {FormatPercent(used)}%"
        : $"{ShortName} unknown";

    public void Apply(UsageWindow window)
    {
        _usedPercent = window.UsedPercent;
        _windowLength = window.WindowLength;
        _resetsAt = window.ResetsAt;
        _isLimitReached = window.IsLimitReached;
        _state = UsageWindowState.Current;
        NotifyUsageChanged();
    }

    public void ApplyLatest(LatestUsageSample sample)
    {
        _usedPercent = sample.UsedPercent;
        _windowLength = sample.WindowLength;
        _resetsAt = sample.ResetsAt;
        _isLimitReached = sample.IsLimitReached;
        _state = UsageWindowState.Stale;
        NotifyUsageChanged();
    }

    public void MarkStale()
    {
        if (_state == UsageWindowState.TrackingOff)
        {
            return;
        }

        _state = IsKnown ? UsageWindowState.Stale : UsageWindowState.Unknown;
        NotifyUsageChanged();
    }

    public void MarkUnknown()
    {
        _usedPercent = null;
        _windowLength = null;
        _resetsAt = null;
        _isLimitReached = false;
        _state = UsageWindowState.Unknown;
        NotifyUsageChanged();
    }

    public void MarkTrackingOff()
    {
        _state = UsageWindowState.TrackingOff;
        NotifyUsageChanged();
    }

    public void SetHistory(IReadOnlyList<UsageHistoryPoint> history, string rangeLabel)
    {
        _history = history;
        _historyRangeLabel = rangeLabel;
        OnPropertyChanged(nameof(History));
        OnPropertyChanged(nameof(HistoryRangeLabel));
        OnPropertyChanged(nameof(HasHistory));
    }

    public void RefreshClock()
    {
        OnPropertyChanged(nameof(ResetText));
    }

    private void NotifyUsageChanged()
    {
        OnPropertyChanged(nameof(UsedPercent));
        OnPropertyChanged(nameof(IsKnown));
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(IsTrackingOff));
        OnPropertyChanged(nameof(IsAtLimit));
        OnPropertyChanged(nameof(IsHighUsage));
        OnPropertyChanged(nameof(IsCriticalUsage));
        OnPropertyChanged(nameof(ShowStatusBadge));
        OnPropertyChanged(nameof(StateKind));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(UsedPercentText));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ResetText));
        OnPropertyChanged(nameof(WindowLengthText));
        OnPropertyChanged(nameof(ToolTipLine));
    }

    private static string FormatPercent(double value)
    {
        return Math.Round(value, 1).ToString("0.#", CultureInfo.CurrentCulture);
    }

    private static string FormatWindowLength(TimeSpan? length)
    {
        if (length is null)
        {
            return "Window length unknown";
        }

        if (length.Value.TotalHours >= 24 && length.Value.TotalHours % 24 == 0)
        {
            return $"{length.Value.TotalDays:0}-day window";
        }

        if (length.Value.TotalHours >= 1 && length.Value.TotalMinutes % 60 == 0)
        {
            return $"{length.Value.TotalHours:0}-hour window";
        }

        return $"{Math.Max(1, length.Value.TotalMinutes):0}-minute window";
    }

    private static string FormatResetText(DateTimeOffset? resetAt, DateTimeOffset now)
    {
        if (resetAt is null)
        {
            return "Reset time unknown";
        }

        var remaining = resetAt.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "Reset due · awaiting refresh";
        }

        string relative;
        if (remaining < TimeSpan.FromMinutes(1))
        {
            relative = "Resets in <1m";
        }
        else if (remaining < TimeSpan.FromHours(1))
        {
            relative = $"Resets in {Math.Max(1, (int)remaining.TotalMinutes)}m";
        }
        else if (remaining < TimeSpan.FromDays(1))
        {
            relative = $"Resets in {(int)remaining.TotalHours}h {remaining.Minutes}m";
        }
        else
        {
            relative = $"Resets in {(int)remaining.TotalDays}d {remaining.Hours}h";
        }

        var localReset = resetAt.Value.ToLocalTime();
        var absolute = localReset.Date == now.LocalDateTime.Date
            ? localReset.ToString("t", CultureInfo.CurrentCulture)
            : localReset.ToString("ddd t", CultureInfo.CurrentCulture);

        return $"{relative} · {absolute}";
    }
}
