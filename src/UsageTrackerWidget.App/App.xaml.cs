using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using UsageTrackerWidget.App.Services;
using UsageTrackerWidget.App.Storage;
using UsageTrackerWidget.App.Themes;
using UsageTrackerWidget.App.Tray;
using UsageTrackerWidget.App.ViewModels;
using UsageTrackerWidget.App.Views;
using UsageTrackerWidget.Providers.Codex;

namespace UsageTrackerWidget.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\UsageTrackerWidget.SingleInstance";
    private const string ShowFlyoutEventName = @"Local\UsageTrackerWidget.ShowFlyout";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showFlyoutEvent;
    private RegisteredWaitHandle? _showFlyoutRegistration;
    private bool _ownsMutex;
    private HttpClient? _httpClient;
    private UsagePollingService? _pollingService;
    private UsageViewModel? _viewModel;
    private UsageFlyoutWindow? _flyoutWindow;
    private TrayController? _trayController;
    private ThemeManager? _themeManager;

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out var createdNew);
        if (!createdNew)
        {
            SignalExistingInstanceToShowFlyout();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        _ownsMutex = true;
        _showFlyoutEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ShowFlyoutEventName);
        _showFlyoutRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showFlyoutEvent,
            OnShowFlyoutSignal,
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        var showFirstRunGuide = false;

        try
        {
            AppPaths.EnsureRootDirectory();

            _themeManager = new ThemeManager();
            _themeManager.Apply();

            var settingsStore = new SettingsStore();
            var settings = settingsStore.Load();
            if (!settingsStore.Exists)
            {
                settingsStore.Save(settings);
                showFirstRunGuide = true;
            }

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(45),
            };

            var tokenManager = new CodexTokenManager(_httpClient);
            var provider = new CodexUsageProvider(
                tokenManager,
                _httpClient,
                imageMeterEnabled: settings.TrackImageAllowance);
            var history = new UsageHistoryRepository();
            var startupRegistration = new StartupRegistration();
            _pollingService = new UsagePollingService(provider);

            _viewModel = new UsageViewModel(
                tokenManager,
                provider,
                _pollingService,
                history,
                settingsStore,
                startupRegistration,
                settings,
                Shutdown);
            _flyoutWindow = new UsageFlyoutWindow(_viewModel);
            _trayController = new TrayController(_viewModel, ToggleFlyout);

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            await _viewModel.InitializeAsync();
            if (_viewModel.IsConnected)
            {
                _pollingService.Start();
            }

            if (showFirstRunGuide)
            {
                ShowFlyout();
            }
        }
        catch (Exception exception)
        {
            var errorWindow = new FatalErrorWindow(
                "Usage Tracker could not start",
                SafeError(exception));
            errorWindow.ShowDialog();
            Shutdown(1);
        }
    }

    private void ToggleFlyout()
    {
        if (_flyoutWindow is null)
        {
            return;
        }

        if (_flyoutWindow.IsVisible)
        {
            _flyoutWindow.Hide();
            return;
        }

        _flyoutWindow.ShowNearTray();
    }

    private void ShowFlyout()
    {
        _flyoutWindow?.ShowNearTray();
    }

    private void OnShowFlyoutSignal(object? state, bool timedOut)
    {
        if (timedOut)
        {
            return;
        }

        Dispatcher.BeginInvoke(ShowFlyout);
    }

    private static void SignalExistingInstanceToShowFlyout()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ShowFlyoutEventName);
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        _viewModel?.ReportUnexpectedError(eventArgs.Exception);
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        _trayController?.Dispose();
        _trayController = null;
        _flyoutWindow?.CloseForExit();
        _flyoutWindow = null;
        _viewModel?.Dispose();
        _viewModel = null;
        _pollingService?.Dispose();
        _pollingService = null;
        _httpClient?.Dispose();
        _httpClient = null;
        _themeManager?.Dispose();
        _themeManager = null;

        _showFlyoutRegistration?.Unregister(null);
        _showFlyoutRegistration = null;
        _showFlyoutEvent?.Dispose();
        _showFlyoutEvent = null;

        if (_ownsMutex)
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        _ownsMutex = false;

        base.OnExit(eventArgs);
    }

    private static string SafeError(Exception exception)
    {
        var message = new string(exception.Message
            .Where(character => !char.IsControl(character))
            .Take(500)
            .ToArray())
            .Trim();
        return message.Length == 0 ? exception.GetType().Name : message;
    }
}
