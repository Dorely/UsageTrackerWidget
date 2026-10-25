# UsageTrackerWidget file map

Read this map at the start of each development session. Generated output, dependencies, and user data are omitted.

| File | Responsibility |
| --- | --- |
| `AGENTS.md` | Stable project workflow and verification instructions. |
| `VISION.md` | Product direction for a glanceable, quota-free AI usage widget. |
| `README.md` | Public setup, behavior, privacy, publishing, and limitations. |
| `CONTRIBUTING.md` | Contribution expectations and local development commands. |
| `LICENSE` | MIT license. |
| `.editorconfig` | Shared whitespace and language indentation defaults. |
| `.gitignore` | Ignore rules for dependencies, build output, logs, and local data. |
| `.github/FUNDING.yml` | Buy Me a Coffee sponsor link. |
| `global.json` | Pins the .NET 10 SDK feature band with latest-feature roll-forward. |
| `UsageTrackerWidget.sln` | Solution containing the contracts, provider, and WPF app. |
| `docs/codex-usage-sources.md` | Codex auth, quota sources, calibration data, and tracker endpoint behavior. |
| `scripts/publish.ps1` | Cleans and publishes a self-contained `win-x64` folder. |
| `scripts/generate-icon.ps1` | Regenerates the multi-resolution application icon. |

## Core contracts

| File | Responsibility |
| --- | --- |
| `src/UsageTrackerWidget.Core/UsageModels.cs` | Provider-neutral snapshots, usage windows, errors, auth exception, and `IUsageProvider`. |
| `src/UsageTrackerWidget.Core/UsageTrackerWidget.Core.csproj` | Dependency-free shared contract library. |

## Codex provider

| File | Responsibility |
| --- | --- |
| `src/UsageTrackerWidget.Providers.Codex/CodexConstants.cs` | OAuth, usage, image-probe, header, timeout, and display constants. |
| `src/UsageTrackerWidget.Providers.Codex/CodexTokens.cs` | Internal stored, live, and request credential shapes. |
| `src/UsageTrackerWidget.Providers.Codex/CodexAuthException.cs` | Public auth errors and internal source-contract error classification. |
| `src/UsageTrackerWidget.Providers.Codex/CodexPkce.cs` | PKCE generation and the Codex authorization URL. |
| `src/UsageTrackerWidget.Providers.Codex/LoopbackOAuthServer.cs` | Loopback-only OAuth callback listener and browser result page. |
| `src/UsageTrackerWidget.Providers.Codex/CodexOAuthService.cs` | Browser authorization, code exchange, and token refresh. |
| `src/UsageTrackerWidget.Providers.Codex/CodexAccountIdentity.cs` | In-memory account-ID extraction from the access-token JWT. |
| `src/UsageTrackerWidget.Providers.Codex/DpapiCodexTokenStore.cs` | Atomic DPAPI-protected credential persistence for the current user. |
| `src/UsageTrackerWidget.Providers.Codex/CodexTokenManager.cs` | Login persistence, early refresh, single-flight refresh, and 401 recovery. |
| `src/UsageTrackerWidget.Providers.Codex/CodexUsageProvider.cs` | Strict parsing of rate-limit and image-meter sources into normalized snapshots. |
| `src/UsageTrackerWidget.Providers.Codex/UsageTrackerWidget.Providers.Codex.csproj` | Windows Codex OAuth/usage transport library. |

## WPF app

| File | Responsibility |
| --- | --- |
| `src/UsageTrackerWidget.App/App.xaml` | Application resources and explicit-shutdown lifecycle. |
| `src/UsageTrackerWidget.App/App.xaml.cs` | Composition root, singleton activation, first-run guide, and cleanup. |
| `src/UsageTrackerWidget.App/AssemblyInfo.cs` | WPF theme-resource lookup metadata. |
| `src/UsageTrackerWidget.App/app.manifest` | Per-monitor DPI awareness and Windows compatibility. |
| `src/UsageTrackerWidget.App/UsageTrackerWidget.App.csproj` | WPF executable, package references, icon, and manifest wiring. |
| `src/UsageTrackerWidget.App/Assets/AppIcon.ico` | Multi-resolution application executable icon. |
| `src/UsageTrackerWidget.App/Themes/Styles.xaml` | Theme brush keys, typography, buttons, inputs, progress, and scrollbars. |
| `src/UsageTrackerWidget.App/Themes/ThemeManager.cs` | Runtime light, dark, and high-contrast palette selection. |
| `src/UsageTrackerWidget.App/Converters/InverseBooleanToVisibilityConverter.cs` | Inverse visibility binding helper. |
| `src/UsageTrackerWidget.App/Tray/DynamicUsageIcon.cs` | Native percentage icon with health/warning border and retained icon stream. |
| `src/UsageTrackerWidget.App/Tray/TrayController.cs` | Tray lifecycle, left-click flyout, tooltip, and right-click menu. |
| `src/UsageTrackerWidget.App/Views/UsageFlyoutWindow.xaml` | Detail flyout, quota cards, sign-in, errors, history, and settings. |
| `src/UsageTrackerWidget.App/Views/UsageFlyoutWindow.xaml.cs` | Flyout positioning, dismissal, and explicit shutdown close. |
| `src/UsageTrackerWidget.App/Views/HistorySparkline.cs` | Custom WPF percent-history chart with reset markers. |
| `src/UsageTrackerWidget.App/Views/FatalErrorWindow.cs` | In-app startup failure surface; no native message box. |
| `src/UsageTrackerWidget.App/ViewModels/UsageViewModel.cs` | State merging, polling events, login/logout, settings, summary, and commands. |
| `src/UsageTrackerWidget.App/ViewModels/UsageWindowViewModel.cs` | Per-window value, state, reset countdown, progress, and history formatting. |
| `src/UsageTrackerWidget.App/ViewModels/UsageIssueViewModel.cs` | Sanitized source-error display model. |
| `src/UsageTrackerWidget.App/Services/UsagePollingService.cs` | Single-flight startup, manual, periodic, and resume refreshes. |
| `src/UsageTrackerWidget.App/Services/StartupRegistration.cs` | Current-user Windows startup entry for the published executable. |
| `src/UsageTrackerWidget.App/Storage/AppPaths.cs` | Local application data paths. |
| `src/UsageTrackerWidget.App/Storage/SettingsStore.cs` | Non-secret local settings persistence. |
| `src/UsageTrackerWidget.App/Storage/UsageHistoryPoint.cs` | History chart and latest-sample records. |
| `src/UsageTrackerWidget.App/Storage/UsageHistoryRepository.cs` | SQLite schema, normalized samples, latest snapshots, and series queries. |
