<h1 align="center">UsageTrackerWidget</h1>

<p align="center">
  A Windows widget that shows how much of your AI service usage limits remain.
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Dorely/UsageTrackerWidget" alt="MIT license" /></a>
  <a href="https://buymeacoffee.com/dorely"><img src="https://img.shields.io/badge/Buy%20me%20a%20coffee-dorely-FFDD00?logo=buymeacoffee&logoColor=black" alt="Buy me a coffee" /></a>
</p>

UsageTrackerWidget is a Windows notification-area widget for AI usage limits. It shows the highest known used percentage in the tray. Click the icon to see the Codex 5-hour and weekly limits, the separate daily image-generation allowance, reset times, and local history.

## Current status

The initial Windows implementation is complete:

- .NET 10 WPF app with a persistent notification-area icon and click-to-open flyout.
- App-owned Codex OAuth login using PKCE and a loopback callback.
- DPAPI-protected credentials for the current Windows user.
- All three current Codex usage windows.
- Ten-minute polling, manual refresh, and refresh after sleep/resume.
- Local SQLite history with window-appropriate sparklines and reset markers.
- Start-with-Windows support and single-instance behavior.

There is one Codex account per installation and no installer yet.

## Requirements

- Windows with the .NET 10 SDK for development.
- A web browser for the OpenAI/Codex sign-in.
- No .NET runtime installation is needed for the published self-contained build.

## Build and run

```powershell
dotnet build UsageTrackerWidget.sln -c Release
dotnet run --project src/UsageTrackerWidget.App/UsageTrackerWidget.App.csproj
```

On first launch, the detail window opens so you can connect Codex. After that, launching the app again while it is running brings the detail window back.

## Publish

```powershell
.\scripts\publish.ps1
.\artifacts\publish\win-x64\UsageTrackerWidget.exe
```

The publish output is a self-contained `win-x64` folder. Copy the whole folder if you move the app.

## Taskbar behavior

Windows controls whether notification-area icons appear beside the clock or in the overflow menu. To keep the widget visible:

1. Open **Taskbar settings**.
2. Open **Other system tray icons**.
3. Turn on **Usage Tracker**.
4. If it remains in overflow, drag its icon onto the taskbar.

The tray icon shows the highest known used percentage. Its border uses green, amber, or red as usage rises, and an amber state when data is stale. Hover for a compact summary or click for details. Right-click for refresh, startup, and exit controls.

## Authentication and privacy

Usage Tracker performs its own OpenAI/Codex sign-in. It never reads, copies, or refreshes Codex CLI's, ImageGenCli's, any other application's tokens, or Lorekeeper's login. Refresh tokens rotate, so sharing another app's token can revoke that app's login.

The sign-in callback binds a short-lived, unused `127.0.0.1` port. It does not use the fixed `localhost:1455` callback, so it can complete while Lorekeeper or another Codex client is running.

- Credentials: `%LOCALAPPDATA%\UsageTrackerWidget\credentials\codex.dat`, protected with Windows DPAPI for the current user.
- History: `%LOCALAPPDATA%\UsageTrackerWidget\usage.db`.
- Settings: `%LOCALAPPDATA%\UsageTrackerWidget\settings.json`.

The history records usage percentages, window lengths, reset times, plan labels, and a local account label. It never stores access tokens, refresh tokens, account IDs, emails, or raw service responses. Identifying fields returned by Codex are dropped before data reaches the UI or database.

The app never sends usage data anywhere except to Codex when checking the quota. There is no telemetry or cloud sync.

## Usage sources

Codex usage checks are read-only and quota-free:

- `GET https://chatgpt.com/backend-api/wham/usage` supplies the 5-hour and weekly windows.
- A deliberately incomplete image-generation request supplies the separate daily image meter. The request omits the required prompt, so the backend rejects it before generating anything. The app accepts the meter only from the expected HTTP 400 response and does not retry it.
- If the image endpoint changes shape, automatic image tracking disables itself and shows the mismatch. Re-enable it from Settings only after reviewing the change.

These are undocumented backend endpoints. See [docs/codex-usage-sources.md](docs/codex-usage-sources.md) for the observed contracts and failure handling.

## Known limitations

- Notification-area placement remains under Windows control; the app cannot force itself beside the clock.
- Image-meter behavior is undocumented and may need maintenance if Codex changes the endpoint.
- History is stored for a single Codex account; multi-account support is deferred.
- There is no installer, auto-update, notifications, or burn-rate projection yet.

## License

[MIT](LICENSE)
