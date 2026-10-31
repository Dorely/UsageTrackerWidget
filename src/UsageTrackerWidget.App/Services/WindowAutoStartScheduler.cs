using System.Globalization;
using UsageTrackerWidget.App.Storage;
using UsageTrackerWidget.Core;

namespace UsageTrackerWidget.App.Services;

internal sealed record WindowAutoStartStatus(string WindowKey, string? Outcome, bool IsError);

// Starts idle usage windows on a schedule. Decisions are made only from a fresh usage read: a due
// start first requests a refresh, and the read that comes back decides whether the window is idle.
internal sealed class WindowAutoStartScheduler : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MissedSlotGrace = TimeSpan.FromHours(1);
    private static readonly TimeSpan FreshReadAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RefreshRequestSpacing = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResetSettleDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StartedCooldown = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan IneffectiveStartBackoff = TimeSpan.FromHours(1);
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromHours(1);

    private readonly IUsageWindowStarter _starter;
    private readonly UsagePollingService _pollingService;
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private UsageSnapshot? _latest;
    private bool _isStarting;
    private Task? _loopTask;
    private bool _disposed;

    public WindowAutoStartScheduler(IUsageWindowStarter starter, UsagePollingService pollingService)
    {
        _starter = starter;
        _pollingService = pollingService;
        _pollingService.ReadCompleted += OnReadCompleted;
        _pollingService.ReadFailed += OnReadFailed;
    }

    public event EventHandler<WindowAutoStartStatus>? StatusChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _loopTask ??= RunLoopAsync(_lifetimeSource.Token);
    }

    // A user edit made after today's start time takes effect tomorrow; only a slot missed while the
    // app was closed or asleep is caught up.
    public void Configure(string windowKey, WindowAutoStartSettings settings, bool isUserChange)
    {
        if (!_starter.CanStart(windowKey))
        {
            return;
        }

        var now = DateTimeOffset.Now;
        WindowAutoStartStatus status;
        lock (_gate)
        {
            var slot = new Slot(windowKey, settings);
            if (isUserChange && !settings.IsRolling && now >= SlotStart(now, settings.Time))
            {
                slot.HandledDate = DateOnly.FromDateTime(now.LocalDateTime);
            }

            _slots[windowKey] = slot;
            status = slot.ToStatus();
        }

        StatusChanged?.Invoke(this, status);
        Evaluate();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Wall-clock checks rather than one long delay, so a slot is noticed promptly after sleep.
            using var timer = new PeriodicTimer(CheckInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                Evaluate();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void OnReadCompleted(object? sender, UsageReadResult result)
    {
        lock (_gate)
        {
            _latest = result.Snapshot;
        }

        Evaluate();
    }

    private void OnReadFailed(object? sender, Exception exception)
    {
        if (exception is not UsageAuthenticationRequiredException)
        {
            return;
        }

        lock (_gate)
        {
            _latest = null;
        }
    }

    private void Evaluate()
    {
        if (_lifetimeSource.IsCancellationRequested)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var refresh = false;
        Slot? toStart = null;
        var statuses = new List<WindowAutoStartStatus>();

        lock (_gate)
        {
            if (_isStarting)
            {
                return;
            }

            foreach (var slot in _slots.Values.Where(slot => slot.Settings.Enabled))
            {
                var action = slot.Settings.IsRolling
                    ? EvaluateRolling(slot, now)
                    : EvaluateTimed(slot, now);

                if (action == SlotAction.Refresh && now - slot.LastRefreshRequestAt >= RefreshRequestSpacing)
                {
                    slot.LastRefreshRequestAt = now;
                    refresh = true;
                }
                else if (action == SlotAction.Start)
                {
                    toStart = slot;
                    break;
                }
                else if (action == SlotAction.Report)
                {
                    statuses.Add(slot.ToStatus());
                }
            }

            if (toStart is not null)
            {
                _isStarting = true;
                toStart.Outcome = "Starting…";
                toStart.OutcomeIsError = false;
                statuses.Add(toStart.ToStatus());
            }
        }

        foreach (var status in statuses)
        {
            StatusChanged?.Invoke(this, status);
        }

        if (toStart is not null)
        {
            _ = RunStartAsync(toStart);
        }
        else if (refresh)
        {
            _ = RequestRefreshAsync();
        }
    }

    private SlotAction EvaluateTimed(Slot slot, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var slotStart = SlotStart(now, slot.Settings.Time);
        if (slot.HandledDate == today || now < slotStart || now >= slotStart + MissedSlotGrace)
        {
            return SlotAction.None;
        }

        if (_latest is null || _latest.ObservedAt < slotStart)
        {
            return SlotAction.Refresh;
        }

        slot.HandledDate = today;
        var window = FindWindow(slot.WindowKey);
        var time = FormatTime(now);
        if (window is null)
        {
            slot.Outcome = $"Skipped {time} · usage unknown";
            slot.OutcomeIsError = true;
            return SlotAction.Report;
        }

        if (!window.IsClockIdle(_latest.ObservedAt))
        {
            slot.Outcome = $"Skipped {time} · already running";
            slot.OutcomeIsError = false;
            return SlotAction.Report;
        }

        return SlotAction.Start;
    }

    private SlotAction EvaluateRolling(Slot slot, DateTimeOffset now)
    {
        if (now < slot.NotBefore || _latest is null || FindWindow(slot.WindowKey) is not { } window)
        {
            return SlotAction.None;
        }

        if (!window.IsClockIdle(_latest.ObservedAt))
        {
            var resetDue = window.ResetsAt is { } resetsAt &&
                now >= resetsAt + ResetSettleDelay &&
                _latest.ObservedAt < resetsAt + ResetSettleDelay;
            return resetDue ? SlotAction.Refresh : SlotAction.None;
        }

        if (now - _latest.ObservedAt > FreshReadAge)
        {
            return SlotAction.Refresh;
        }

        if (slot.LastStartedAt is { } lastStarted && now - lastStarted < IneffectiveStartBackoff)
        {
            slot.NotBefore = lastStarted + IneffectiveStartBackoff;
            slot.Outcome = $"Sent {FormatTime(lastStarted)} but the window stayed idle · retrying {FormatTime(slot.NotBefore)}";
            slot.OutcomeIsError = true;
            return SlotAction.Report;
        }

        return SlotAction.Start;
    }

    private async Task RunStartAsync(Slot slot)
    {
        var startedAt = DateTimeOffset.Now;
        string outcome;
        var isError = true;
        try
        {
            await _starter.StartAsync(slot.WindowKey, _lifetimeSource.Token);
            outcome = $"Started {FormatTime(startedAt)}";
            isError = false;
        }
        catch (OperationCanceledException) when (_lifetimeSource.IsCancellationRequested)
        {
            return;
        }
        catch (UsageWindowStartException exception)
        {
            outcome = $"Failed {FormatTime(startedAt)} · {exception.Message}";
        }
        catch (UsageAuthenticationRequiredException)
        {
            outcome = $"Failed {FormatTime(startedAt)} · sign in to Codex again";
        }
        catch (Exception exception)
        {
            outcome = $"Failed {FormatTime(startedAt)} · {exception.GetType().Name}";
        }

        WindowAutoStartStatus status;
        lock (_gate)
        {
            _isStarting = false;
            slot.Outcome = outcome;
            slot.OutcomeIsError = isError;
            if (isError)
            {
                slot.NotBefore = startedAt + FailureBackoff;
            }
            else
            {
                slot.LastStartedAt = startedAt;
                slot.NotBefore = startedAt + StartedCooldown;
            }

            status = slot.ToStatus();
        }

        StatusChanged?.Invoke(this, status);
        await RequestRefreshAsync();
    }

    private async Task RequestRefreshAsync()
    {
        if (_lifetimeSource.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await _pollingService.RefreshNowAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private UsageWindow? FindWindow(string windowKey)
    {
        return _latest?.Windows.LastOrDefault(window => window.Key == windowKey);
    }

    private static DateTimeOffset SlotStart(DateTimeOffset now, TimeOnly time)
    {
        var local = DateOnly.FromDateTime(now.LocalDateTime).ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static string FormatTime(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollingService.ReadCompleted -= OnReadCompleted;
        _pollingService.ReadFailed -= OnReadFailed;
        _lifetimeSource.Cancel();
    }

    private enum SlotAction
    {
        None,
        Refresh,
        Start,
        Report,
    }

    private sealed class Slot
    {
        public Slot(string windowKey, WindowAutoStartSettings settings)
        {
            WindowKey = windowKey;
            Settings = settings;
        }

        public string WindowKey { get; }

        public WindowAutoStartSettings Settings { get; }

        public DateOnly? HandledDate { get; set; }

        public DateTimeOffset NotBefore { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset LastRefreshRequestAt { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset? LastStartedAt { get; set; }

        public string? Outcome { get; set; }

        public bool OutcomeIsError { get; set; }

        public WindowAutoStartStatus ToStatus()
        {
            return new WindowAutoStartStatus(WindowKey, Outcome, OutcomeIsError);
        }
    }
}
