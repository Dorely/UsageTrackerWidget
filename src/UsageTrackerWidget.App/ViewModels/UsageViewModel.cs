using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using UsageTrackerWidget.App.Services;
using UsageTrackerWidget.App.Storage;
using UsageTrackerWidget.Core;
using UsageTrackerWidget.Providers.Codex;

namespace UsageTrackerWidget.App.ViewModels;

internal sealed partial class UsageViewModel : ObservableObject, IDisposable
{
    private readonly CodexTokenManager _tokenManager;
    private readonly CodexUsageProvider _provider;
    private readonly UsagePollingService _pollingService;
    private readonly UsageHistoryRepository _history;
    private readonly SettingsStore _settingsStore;
    private readonly StartupRegistration _startupRegistration;
    private readonly Action _exitAction;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _clockTimer;
    private readonly CancellationTokenSource _lifetimeSource = new();
    private AppSettings _settings;
    private DateTimeOffset? _lastSuccessfulAt;
    private bool _suppressSettingsSideEffects;
    private CancellationTokenSource? _loginSource;
    private bool _disposed;
    private bool _hasIssues;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnectPanel))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool isRefreshing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(ShowConnectPanel))]
    private bool isLoginInProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool isInitializing;

    [ObservableProperty]
    private string connectionText = "Starting…";

    [ObservableProperty]
    private string planText = "Plan unknown";

    [ObservableProperty]
    private string accountText = "Default account";

    [ObservableProperty]
    private string lastUpdatedText = "Not checked yet";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoginMessage))]
    private string loginMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoginUrl))]
    private string loginUrl = string.Empty;

    [ObservableProperty]
    private bool showSettings;

    [ObservableProperty]
    private bool isConfirmingDisconnect;

    [ObservableProperty]
    private bool startWithWindows;

    [ObservableProperty]
    private bool trackImageAllowance;

    [ObservableProperty]
    private bool isStale;

    [ObservableProperty]
    private double? iconPercent;

    [ObservableProperty]
    private bool iconWarning;

    [ObservableProperty]
    private string toolTipText = "Codex · starting";

    public UsageViewModel(
        CodexTokenManager tokenManager,
        CodexUsageProvider provider,
        UsagePollingService pollingService,
        UsageHistoryRepository history,
        SettingsStore settingsStore,
        StartupRegistration startupRegistration,
        AppSettings settings,
        Action exitAction)
    {
        _tokenManager = tokenManager;
        _provider = provider;
        _pollingService = pollingService;
        _history = history;
        _settingsStore = settingsStore;
        _startupRegistration = startupRegistration;
        _settings = settings;
        _exitAction = exitAction;
        _dispatcher = Application.Current.Dispatcher;

        Windows =
        [
            new UsageWindowViewModel(UsageWindowKeys.CodexSession, "5-hour limit", "5h"),
            new UsageWindowViewModel(UsageWindowKeys.CodexWeekly, "Weekly limit", "Week"),
            new UsageWindowViewModel(UsageWindowKeys.CodexDailyImages, "Daily image allowance", "Images"),
        ];

        _suppressSettingsSideEffects = true;
        StartWithWindows = settings.StartWithWindows;
        TrackImageAllowance = settings.TrackImageAllowance;
        _suppressSettingsSideEffects = false;

        _pollingService.ReadCompleted += OnReadCompleted;
        _pollingService.ReadFailed += OnReadFailed;
        _pollingService.RefreshingChanged += OnRefreshingChanged;

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _clockTimer.Tick += OnClockTick;

        if (!TrackImageAllowance)
        {
            ImageWindow.MarkTrackingOff();
        }

        UpdateSummary();
    }

    public ObservableCollection<UsageWindowViewModel> Windows { get; }

    public ObservableCollection<UsageIssueViewModel> Issues { get; } = [];

    public bool ShowConnectPanel => !IsConnected;

    public bool HasLoginMessage => !string.IsNullOrWhiteSpace(LoginMessage);

    public bool HasLoginUrl => !string.IsNullOrWhiteSpace(LoginUrl);

    public bool IsBusy => IsRefreshing || IsLoginInProgress || IsInitializing;

    public bool HasIssues => _hasIssues;

    public string RefreshIntervalText => "Every 10 minutes";

    private UsageWindowViewModel ImageWindow =>
        Windows.First(window => window.Key == UsageWindowKeys.CodexDailyImages);

    public async Task InitializeAsync()
    {
        IsInitializing = true;
        try
        {
            try
            {
                _startupRegistration.SetEnabled(StartWithWindows);
            }
            catch (Exception exception)
            {
                AddIssue(
                    "Start with Windows",
                    "The Windows startup entry could not be updated.",
                    SafeExceptionDetail(exception),
                    false);
            }

            var latest = await LoadLatestSamplesAsync();
            if (latest.Count > 0)
            {
                ApplyLatestSamples(latest);
            }

            try
            {
                IsConnected = await _tokenManager.HasStoredCredentialAsync(_lifetimeSource.Token);
            }
            catch (CodexAuthException exception)
            {
                IsConnected = false;
                LoginMessage = exception.Message;
                AddIssue("Codex credentials", exception.Message, "Local protected storage", false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                IsConnected = false;
                LoginMessage = "Codex credentials could not be read. Sign in again.";
                AddIssue("Codex credentials", LoginMessage, "Local protected storage", false);
            }

            if (!TrackImageAllowance)
            {
                ImageWindow.MarkTrackingOff();
            }

            ConnectionText = BuildConnectionText();
            IsStale = IsConnected
                ? latest.Count == 0 || _lastSuccessfulAt is null
                : latest.Count > 0;
            await RefreshAllHistoriesAsync(_lifetimeSource.Token);
            UpdateSummary();
        }
        finally
        {
            IsInitializing = false;
            LastUpdatedText = BuildLastUpdatedText();
            _clockTimer.Start();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        await _pollingService.RefreshNowAsync();
    }

    private bool CanRefresh()
    {
        return IsConnected && !IsRefreshing && !IsLoginInProgress;
    }

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        if (IsLoginInProgress)
        {
            return;
        }

        IsLoginInProgress = true;
        IsConfirmingDisconnect = false;
        LoginUrl = string.Empty;
        LoginMessage = "Starting secure browser sign-in…";
        _loginSource = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeSource.Token);

        try
        {
            await _tokenManager.LoginAsync(
                (url, browserOpened) => PostAuthorizationState(url, browserOpened),
                _loginSource.Token);

            if (_loginSource.IsCancellationRequested)
            {
                return;
            }

            IsConnected = true;
            LoginMessage = "Connected. Reading Codex usage…";
            ConnectionText = BuildConnectionText();
            UpdateSummary();
            _pollingService.Start();
            await _pollingService.RefreshNowAsync();
        }
        catch (OperationCanceledException) when (_loginSource.IsCancellationRequested)
        {
            LoginMessage = "Sign-in canceled.";
        }
        catch (Exception exception) when (exception is CodexAuthException or CodexReauthRequiredException)
        {
            LoginMessage = exception.Message;
            AddIssue("Codex sign-in", exception.Message, "OAuth", false);
        }
        catch (HttpRequestException)
        {
            LoginMessage = "Could not reach Codex sign-in. Check the network connection and try again.";
        }
        catch (Exception)
        {
            LoginMessage = "Codex sign-in failed. Try again.";
        }
        finally
        {
            _loginSource?.Dispose();
            _loginSource = null;
            IsLoginInProgress = false;
            ConnectionText = BuildConnectionText();
            UpdateSummary();
        }
    }

    private bool CanSignIn()
    {
        return !IsLoginInProgress && !IsInitializing;
    }

    [RelayCommand]
    private void CancelSignIn()
    {
        _loginSource?.Cancel();
    }

    [RelayCommand]
    private void OpenSignInPage()
    {
        if (string.IsNullOrWhiteSpace(LoginUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(LoginUrl) { UseShellExecute = true });
        }
        catch (Exception)
        {
            LoginMessage = "The sign-in page could not be opened. Copy the link into your browser.";
        }
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        ShowSettings = !ShowSettings;
        if (ShowSettings)
        {
            IsConfirmingDisconnect = false;
        }
    }

    [RelayCommand]
    private void RequestDisconnect()
    {
        IsConfirmingDisconnect = true;
    }

    [RelayCommand]
    private void CancelDisconnect()
    {
        IsConfirmingDisconnect = false;
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        try
        {
            await _tokenManager.ClearAsync(_lifetimeSource.Token);
            IsConnected = false;
            IsConfirmingDisconnect = false;
            PlanText = "Plan unknown";
            LoginMessage = "Codex disconnected.";
            _lastSuccessfulAt = null;
            ClearIssues();
            foreach (var window in Windows)
            {
                window.MarkStale();
            }

            IsStale = true;
            ConnectionText = BuildConnectionText();
            LastUpdatedText = BuildLastUpdatedText();
            UpdateSummary();
        }
        catch (Exception)
        {
            IsConfirmingDisconnect = false;
            AddIssue("Codex credentials", "The stored Codex credential could not be removed.", "Local storage", false);
        }
    }

    [RelayCommand]
    private void Exit()
    {
        _exitAction();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_suppressSettingsSideEffects)
        {
            return;
        }

        _settings = _settings with { StartWithWindows = value };
        SaveSettings();
        try
        {
            _startupRegistration.SetEnabled(value);
        }
        catch (Exception exception)
        {
            AddIssue(
                "Start with Windows",
                "The Windows startup entry could not be changed.",
                SafeExceptionDetail(exception),
                false);
        }
    }

    partial void OnTrackImageAllowanceChanged(bool value)
    {
        _provider.ImageMeterEnabled = value;
        if (value)
        {
            ImageWindow.MarkStale();
        }
        else
        {
            ImageWindow.MarkTrackingOff();
        }

        if (_suppressSettingsSideEffects)
        {
            UpdateSummary();
            return;
        }

        _settings = _settings with { TrackImageAllowance = value };
        SaveSettings();
        UpdateSummary();

        if (value && IsConnected)
        {
            _ = _pollingService.RefreshNowAsync();
        }
    }

    partial void OnIsRefreshingChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        SignInCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsBusy));
    }

    partial void OnIsConnectedChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        SignInCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowConnectPanel));
    }

    partial void OnIsLoginInProgressChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        SignInCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsBusy));
    }

    partial void OnIsInitializingChanged(bool value)
    {
        SignInCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsBusy));
    }

    partial void OnLoginMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasLoginMessage));
    }

    partial void OnLoginUrlChanged(string value)
    {
        OnPropertyChanged(nameof(HasLoginUrl));
    }

    private void PostAuthorizationState(string url, bool browserOpened)
    {
        _dispatcher.BeginInvoke(() =>
        {
            LoginUrl = url;
            LoginMessage = browserOpened
                ? "Complete sign-in in your browser. This window will update when Codex is connected."
                : "The browser did not open. Use the link below to continue sign-in.";
        });
    }

    private void OnReadCompleted(object? sender, UsageReadResult result)
    {
        _dispatcher.BeginInvoke(() => _ = ApplyReadResultAsync(result));
    }

    private void OnReadFailed(object? sender, Exception exception)
    {
        _dispatcher.BeginInvoke(() => _ = HandleReadFailureAsync(exception));
    }

    private void OnRefreshingChanged(object? sender, bool isRefreshing)
    {
        _dispatcher.BeginInvoke(() => IsRefreshing = isRefreshing);
    }

    private async Task ApplyReadResultAsync(UsageReadResult result)
    {
        try
        {
            IsConnected = true;
            AccountText = "Default account";
            if (!string.IsNullOrWhiteSpace(result.Snapshot.PlanLabel))
            {
                PlanText = FormatPlanLabel(result.Snapshot.PlanLabel);
            }

            var contractChanged = result.Errors.Any(error =>
                error.SourceKey == "codex.image-meter" &&
                error.Kind == UsageErrorKind.ContractChanged);
            if (contractChanged && TrackImageAllowance)
            {
                DisableImageTrackingAutomatically();
            }

            var currentWindows = result.Snapshot.Windows
                .GroupBy(window => window.Key)
                .ToDictionary(group => group.Key, group => group.Last());

            foreach (var window in Windows)
            {
                if (window.Key == UsageWindowKeys.CodexDailyImages && !TrackImageAllowance)
                {
                    window.MarkTrackingOff();
                }
                else if (currentWindows.TryGetValue(window.Key, out var current))
                {
                    window.Apply(current);
                }
                else
                {
                    window.MarkStale();
                }
            }

            if (result.Snapshot.Windows.Count > 0)
            {
                _lastSuccessfulAt = result.Snapshot.ObservedAt;
            }

            ReplaceIssues(result.Errors.Select(error => new UsageIssueViewModel(
                error.DisplayName,
                error.Message,
                BuildIssueDetail(error),
                error.Kind == UsageErrorKind.InvalidResponse)));

            IsStale = result.Errors.Count > 0 ||
                Windows.Any(window =>
                    !window.IsTrackingOff &&
                    (window.IsStale || !window.IsKnown));
            ConnectionText = BuildConnectionText();
            LastUpdatedText = BuildLastUpdatedText();

            try
            {
                await _history.SaveAsync(result.Snapshot, _lifetimeSource.Token);
                await RefreshAllHistoriesAsync(_lifetimeSource.Token);
            }
            catch (Exception exception) when (
                exception is SqliteException or IOException or UnauthorizedAccessException)
            {
                AddIssue(
                    "Usage history",
                    "The latest sample could not be written to local history.",
                    SafeExceptionDetail(exception),
                    false);
            }

            UpdateSummary();
        }
        catch (OperationCanceledException) when (_lifetimeSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AddIssue(
                "Codex usage",
                "The refreshed usage data could not be applied.",
                SafeExceptionDetail(exception),
                false);
            IsStale = true;
            UpdateSummary();
        }
    }

    private async Task HandleReadFailureAsync(Exception exception)
    {
        try
        {
            if (exception is UsageAuthenticationRequiredException or CodexReauthRequiredException)
            {
                IsConnected = false;
                LoginMessage = exception.Message;
                foreach (var window in Windows)
                {
                    window.MarkStale();
                }

                IsStale = true;
                ConnectionText = BuildConnectionText();
                ReplaceIssues(
                [
                    new UsageIssueViewModel(
                        "Codex sign-in",
                        exception.Message,
                        "Credentials are missing or expired",
                        false),
                ]);
                UpdateSummary();
                return;
            }

            if (exception is CodexAuthException authException)
            {
                AddIssue(
                    "Codex sign-in",
                    authException.Message,
                    "Authentication",
                    false);
                IsStale = true;
                UpdateSummary();
                return;
            }

            AddIssue(
                "Codex usage",
                "Usage could not be refreshed.",
                SafeExceptionDetail(exception),
                false);
            IsStale = true;
            UpdateSummary();
        }
        catch (Exception)
        {
            IsStale = true;
            UpdateSummary();
        }
    }

    private void DisableImageTrackingAutomatically()
    {
        _suppressSettingsSideEffects = true;
        TrackImageAllowance = false;
        _suppressSettingsSideEffects = false;
        _provider.ImageMeterEnabled = false;
        ImageWindow.MarkTrackingOff();
        _settings = _settings with { TrackImageAllowance = false };
        SaveSettings();
    }

    private async Task<Dictionary<string, LatestUsageSample>> LoadLatestSamplesAsync()
    {
        try
        {
            return (await _history.LoadLatestAsync(_lifetimeSource.Token))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }
        catch (Exception exception) when (
            exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            AddIssue(
                "Usage history",
                "Local usage history could not be opened. Live usage can still refresh.",
                SafeExceptionDetail(exception),
                false);
            return new Dictionary<string, LatestUsageSample>(StringComparer.Ordinal);
        }
    }

    private void ApplyLatestSamples(IReadOnlyDictionary<string, LatestUsageSample> latest)
    {
        foreach (var window in Windows)
        {
            if (latest.TryGetValue(window.Key, out var sample))
            {
                window.ApplyLatest(sample);
            }
        }

        var newest = latest.Values
            .OrderByDescending(sample => sample.ObservedAt)
            .FirstOrDefault();
        if (newest is not null)
        {
            _lastSuccessfulAt = newest.ObservedAt;
            if (!string.IsNullOrWhiteSpace(newest.PlanLabel))
            {
                PlanText = FormatPlanLabel(newest.PlanLabel);
            }
        }
    }

    private async Task RefreshAllHistoriesAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var window in Windows)
        {
            var since = window.Key == UsageWindowKeys.CodexWeekly
                ? now.AddDays(-7)
                : now.AddHours(-24);
            try
            {
                var points = await _history.LoadSeriesAsync(
                    window.Key,
                    since,
                    maximumPoints: 600,
                    cancellationToken);
                window.SetHistory(
                    points,
                    window.Key == UsageWindowKeys.CodexWeekly ? "Last 7 days" : "Last 24 hours");
            }
            catch (Exception exception) when (
                exception is SqliteException or IOException or UnauthorizedAccessException)
            {
                window.SetHistory([], string.Empty);
            }
        }
    }

    private void OnClockTick(object? sender, EventArgs eventArgs)
    {
        foreach (var window in Windows)
        {
            window.RefreshClock();
        }

        LastUpdatedText = BuildLastUpdatedText();
    }

    private void UpdateSummary()
    {
        var knownWindows = Windows
            .Where(window => !window.IsTrackingOff && window.IsKnown)
            .ToList();

        IconPercent = knownWindows.Count > 0
            ? knownWindows.Max(window => window.UsedPercent)
            : null;
        IconWarning = !IsConnected || IsStale || HasIssues;

        if (IsLoginInProgress)
        {
            ToolTipText = "Codex · sign-in in progress";
            return;
        }

        if (!IsConnected)
        {
            ToolTipText = knownWindows.Count > 0
                ? WithLengthLimit($"Codex · sign in required · {BuildWindowSummary(knownWindows)}")
                : "Codex · sign in required";
            return;
        }

        if (knownWindows.Count == 0)
        {
            ToolTipText = IsStale ? "Codex · usage unavailable" : "Codex · waiting for usage";
            return;
        }

        var prefix = IconWarning ? "Codex (stale)" : "Codex";
        var text = $"{prefix} · {BuildWindowSummary(Windows)}";
        ToolTipText = WithLengthLimit(text);
    }

    private static string BuildWindowSummary(IEnumerable<UsageWindowViewModel> windows)
    {
        return string.Join(
            " · ",
            windows.Select(window => window.IsTrackingOff
                ? $"{window.ShortName} off"
                : window.UsedPercent is { } used
                    ? $"{window.ShortName} {used.ToString("0.#", CultureInfo.CurrentCulture)}%"
                    : $"{window.ShortName} ?"));
    }

    private string BuildConnectionText()
    {
        if (!IsConnected)
        {
            return "Not connected";
        }

        if (IsRefreshing && _lastSuccessfulAt is null)
        {
            return "Connecting…";
        }

        return IsStale ? "Connected · stale data" : "Connected";
    }

    private string BuildLastUpdatedText()
    {
        if (_lastSuccessfulAt is null)
        {
            return "Not checked yet";
        }

        var elapsed = DateTimeOffset.UtcNow - _lastSuccessfulAt.Value;
        var age = elapsed switch
        {
            { TotalSeconds: < 45 } => "Updated just now",
            { TotalMinutes: < 1 } => "Updated <1m ago",
            { TotalMinutes: < 60 } => $"Updated {(int)elapsed.TotalMinutes}m ago",
            { TotalHours: < 24 } => $"Updated {(int)elapsed.TotalHours}h ago",
            _ => $"Updated {(int)elapsed.TotalDays}d ago",
        };

        return IsStale ? $"{age} · stale" : age;
    }

    private static string FormatPlanLabel(string plan)
    {
        var normalized = plan.Replace('_', ' ').Trim();
        if (normalized.Length == 0)
        {
            return "Plan unknown";
        }

        return $"{char.ToUpper(normalized[0], CultureInfo.CurrentCulture)}{normalized[1..]} plan";
    }

    private static string BuildIssueDetail(UsageSourceError error)
    {
        var status = error.StatusCode is { } code ? $" · HTTP {code}" : string.Empty;
        return $"{error.DisplayName}{status}";
    }

    private void ReplaceIssues(IEnumerable<UsageIssueViewModel> issues)
    {
        Issues.Clear();
        foreach (var issue in issues)
        {
            Issues.Add(issue);
        }

        SetHasIssues(Issues.Count > 0);
    }

    private void AddIssue(string title, string message, string detail, bool isWarning)
    {
        Issues.Add(new UsageIssueViewModel(title, message, detail, isWarning));
        SetHasIssues(true);
    }

    private void ClearIssues()
    {
        Issues.Clear();
        SetHasIssues(false);
    }

    private void SetHasIssues(bool value)
    {
        if (_hasIssues == value)
        {
            return;
        }

        _hasIssues = value;
        OnPropertyChanged(nameof(HasIssues));
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddIssue(
                "Settings",
                "Local settings could not be saved.",
                SafeExceptionDetail(exception),
                false);
        }
    }

    private static string SafeExceptionDetail(Exception exception)
    {
        var message = new string(exception.Message
            .Where(character => !char.IsControl(character))
            .Take(180)
            .ToArray())
            .Trim();
        return message.Length == 0 ? exception.GetType().Name : message;
    }

    private static string WithLengthLimit(string value)
    {
        const int limit = 120;
        return value.Length <= limit ? value : $"{value[..(limit - 1)]}…";
    }

    internal void ReportUnexpectedError(Exception exception)
    {
        AddIssue(
            "Usage Tracker",
            "An unexpected error occurred. The app is still running.",
            SafeExceptionDetail(exception),
            false);
        UpdateSummary();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clockTimer.Stop();
        _clockTimer.Tick -= OnClockTick;
        _pollingService.ReadCompleted -= OnReadCompleted;
        _pollingService.ReadFailed -= OnReadFailed;
        _pollingService.RefreshingChanged -= OnRefreshingChanged;
        _lifetimeSource.Cancel();
        _loginSource?.Cancel();
        _loginSource?.Dispose();
        _lifetimeSource.Dispose();
    }
}
