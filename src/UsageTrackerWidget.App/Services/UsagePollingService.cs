using Microsoft.Win32;
using UsageTrackerWidget.Core;

namespace UsageTrackerWidget.App.Services;

internal sealed class UsagePollingService : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(10);

    private readonly IUsageProvider _provider;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private Task? _loopTask;
    private bool _disposed;

    public UsagePollingService(IUsageProvider provider, TimeSpan? interval = null)
    {
        _provider = provider;
        _interval = interval ?? DefaultInterval;
    }

    public event EventHandler<UsageReadResult>? ReadCompleted;

    public event EventHandler<Exception>? ReadFailed;

    public event EventHandler<bool>? RefreshingChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loopTask is not null)
        {
            return;
        }

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _loopTask = RunLoopAsync(_lifetimeSource.Token);
    }

    public async Task<bool> RefreshNowAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await RefreshCoreAsync(_lifetimeSource.Token);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshCoreAsync(cancellationToken);
            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshCoreAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken))
        {
            return false;
        }

        try
        {
            RefreshingChanged?.Invoke(this, true);
            var result = await _provider.ReadAsync(cancellationToken);
            ReadCompleted?.Invoke(this, result);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            ReadFailed?.Invoke(this, exception);
            return false;
        }
        finally
        {
            RefreshingChanged?.Invoke(this, false);
            _refreshGate.Release();
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode == PowerModes.Resume && !_lifetimeSource.IsCancellationRequested)
        {
            _ = RefreshCoreAsync(_lifetimeSource.Token);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _lifetimeSource.Cancel();
    }
}
