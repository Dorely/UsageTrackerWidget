# Contributing to UsageTrackerWidget

Thanks for helping make AI usage limits visible.

## Before starting

- Search existing issues before opening a bug report or feature request.
- Keep proposals aligned with the direction in `VISION.md`: usage checks stay read-only and quota-free, and data stays local.
- Read `AGENTS.md` and `FILEMAP.md` for the project's conventions and layout.

## Development

- Install the .NET 10 SDK.
- Build with `dotnet build UsageTrackerWidget.sln -c Release`.
- Run with `dotnet run --project src/UsageTrackerWidget.App/UsageTrackerWidget.App.csproj`.
- Publish the self-contained Windows build with `.\scripts\publish.ps1`.
- Run the app after UI changes and record which interactions were checked. This project intentionally has no automated test project unless the maintainer requests one.
- Never commit local credential, settings, or history files from `%LOCALAPPDATA%\UsageTrackerWidget`.

## Pull requests

- Keep each pull request focused and describe the user-visible behavior it changes.
- Update `README.md`, `FILEMAP.md`, and `docs/` when responsibilities or behavior change.
- Record new findings about undocumented service endpoints in `docs/`, with the date observed.
- Do not include real credentials, tokens, account IDs, emails, screenshots showing personal account details, or build output.
