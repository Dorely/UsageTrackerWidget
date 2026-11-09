using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using UsageTrackerWidget.App.Storage;
using UsageTrackerWidget.Providers.Codex;

namespace UsageTrackerWidget.App.ViewModels;

internal sealed partial class BankedResetsViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan LeadTime = TimeSpan.FromMinutes(30);
    private readonly CodexResetService _service;
    private readonly Action<bool> _saveEnabled;
    private readonly Action _refreshUsage;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ResetAttemptStore? _journal;
    private bool _connected;
    private bool _busy;
    private bool _disposed;
    private int _connectionVersion;
    private DateTimeOffset _nextRead = DateTimeOffset.MinValue;
    private CodexResetInventory? _inventory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    private bool enabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    private bool verified;

    [ObservableProperty]
    private string summary = "Connect Codex to check banked resets.";

    [ObservableProperty]
    private string outcome = string.Empty;

    public BankedResetsViewModel(CodexResetService service, bool enabled, Action<bool> saveEnabled, Action refreshUsage)
    {
        _service = service;
        _saveEnabled = saveEnabled;
        _refreshUsage = refreshUsage;
        this.enabled = enabled;
        try
        {
            _journal = new ResetAttemptStore();
            if (_journal.LastAttempt is { } last)
            {
                Outcome = FormatOutcome(last.Outcome);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Summary = "Reset automation unavailable: the local attempt journal could not be read.";
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public ObservableCollection<string> Details { get; } = [];
    public bool CanToggle => Enabled || (Verified && _connected && _journal is not null);

    public void SetConnected(bool connected)
    {
        _connectionVersion++;
        _connected = connected;
        OnPropertyChanged(nameof(CanToggle));
        if (!connected)
        {
            Verified = false;
            _inventory = null;
            Details.Clear();
            Summary = "Connect Codex to check banked resets.";
            return;
        }

        _nextRead = DateTimeOffset.MinValue;
        _ = CheckAsync();
    }

    public void Refresh() { _nextRead = DateTimeOffset.MinValue; _ = CheckAsync(); }

    partial void OnEnabledChanged(bool value)
    {
        _saveEnabled(value);
        if (value)
        {
            _nextRead = DateTimeOffset.MinValue;
            _ = CheckAsync();
        }
        UpdateDetails();
    }

    private void OnTick(object? sender, EventArgs e) => _ = CheckAsync();

    private async Task CheckAsync()
    {
        if (_disposed || !_connected || _busy || _journal is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var due = Verified && Enabled && _inventory?.Credits.Any(credit =>
            credit.ExpiresAt is { } expiry && now < expiry && now >= expiry - LeadTime &&
            _journal.Find(ResetAttemptStore.Hash(credit.Id)) is null) == true;
        if (now < _nextRead && !due)
        {
            return;
        }

        _busy = true;
        var connectionVersion = _connectionVersion;
        // Avoid tight retry loops after network and service errors.
        _nextRead = now.AddMinutes(1);
        try
        {
            var inventory = await _service.ReadAsync(_lifetime.Token);
            if (!_connected || _disposed || connectionVersion != _connectionVersion) return;
            _inventory = inventory;
            Verified = true;
            _nextRead = DateTimeOffset.UtcNow.AddMinutes(10);
            Summary = inventory.AvailableCount == 1 ? "1 banked reset available" : $"{inventory.AvailableCount} banked resets available";
            var availableHashes = inventory.Credits.Select(credit => ResetAttemptStore.Hash(credit.Id)).ToHashSet(StringComparer.Ordinal);
            if (_journal.Pending.Any(attempt => !availableHashes.Contains(attempt.CreditHash)))
            {
                Outcome = "A previous attempt could not be confirmed; check Codex reset history.";
            }
            UpdateDetails();

            foreach (var credit in inventory.Credits.OrderBy(item => item.ExpiresAt))
            {
                now = DateTimeOffset.UtcNow;
                if (!Enabled || !_connected || _disposed || connectionVersion != _connectionVersion) break;
                if (credit.ExpiresAt is not { } expiry || now >= expiry || now < expiry - LeadTime) continue;
                var hash = ResetAttemptStore.Hash(credit.Id);
                var previous = _journal.Find(hash);
                if (previous is not null && previous.Outcome != "pending") continue;

                var attempt = previous ?? _journal.Prepare(hash, expiry);
                // Persist the request ID before sending so recovery always uses the same operation.
                if (DateTimeOffset.UtcNow >= expiry) continue;
                var result = await _service.ConsumeAsync(credit.Id, attempt.RequestId, _lifetime.Token);
                _journal.Complete(attempt, result);
                Outcome = FormatOutcome(result);
                _refreshUsage();
                // Never act on another reset until the account has been read again.
                _nextRead = DateTimeOffset.MinValue;
                break;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (CodexReauthRequiredException)
        {
            Verified = false;
            Summary = "Reset check unavailable: sign in to Codex again.";
        }
        catch (CodexAuthException)
        {
            Verified = false;
            Summary = "Reset check unavailable: the local Codex credentials could not be read.";
        }
        catch (CodexResetException exception)
        {
            Verified = false;
            Summary = $"Reset check unavailable: {exception.Message}";
        }
        catch (HttpRequestException)
        {
            Verified = false;
            Summary = "Reset check unavailable: could not reach Codex.";
        }
        catch (OperationCanceledException)
        {
            Verified = false;
            Summary = "Reset check unavailable: the request timed out.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Verified = false;
            Summary = "Reset automation unavailable: the local attempt journal could not be saved.";
        }
        catch (Exception exception)
        {
            Verified = false;
            Summary = $"Reset check unavailable ({exception.GetType().Name}).";
        }
        finally
        {
            _busy = false;
            UpdateDetails();
        }
    }

    private static string FormatOutcome(string result) => result switch
    {
        "reset" => "Banked reset applied.",
        "already_redeemed" => "Banked reset was already applied.",
        "nothing_to_reset" => "Reset not applied: no eligible usage to reset. It will not be retried.",
        "no_credit" => "Reset was no longer available.",
        _ => "A previous reset attempt is pending reconciliation.",
    };

    private void UpdateDetails()
    {
        Details.Clear();
        if (_inventory is null || _journal is null) return;
        foreach (var credit in _inventory.Credits.OrderBy(item => item.ExpiresAt ?? DateTimeOffset.MaxValue))
        {
            if (credit.ExpiresAt is not { } expiry)
            {
                Details.Add("Expiry unknown · no automatic attempt scheduled");
                continue;
            }

            var attempt = _journal.Find(ResetAttemptStore.Hash(credit.Id));
            var state = attempt?.Outcome switch
            {
                "pending" => "Attempt pending reconciliation",
                "nothing_to_reset" => "Not eligible · no retry",
                "reset" or "already_redeemed" => "Applied · awaiting refresh",
                "no_credit" => "No longer available",
                _ when Enabled => $"Auto-apply {expiry.Add(-LeadTime).ToLocalTime():g}",
                _ => "Auto-apply off",
            };
            Details.Add($"Expires {expiry.ToLocalTime():g} · {state}");
        }

        if (_inventory.AvailableCount > _inventory.Credits.Count)
        {
            Details.Add("Some reset details are unavailable · those resets cannot be scheduled");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _lifetime.Cancel();
    }
}
