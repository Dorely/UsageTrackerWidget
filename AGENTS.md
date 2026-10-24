# UsageTrackerWidget - Project Guidelines

## First Read
- Read `VISION.md` immediately before doing any substantive work in this repo.
- Read `FILEMAP.md` at the start of every session to understand the full codebase layout.
- Treat `VISION.md` as the high-level direction for the project.
- Do not assume everything in `VISION.md` is implemented or currently part of the implementation plan. Use the codebase to confirm current behavior.
- Keep this file (`AGENTS.md`) stable and high level. Do not add notes here that are likely to become stale during normal development.

## Git Workflow
- Before starting a task, inspect the current branch, upstream, and working tree, including untracked files. Fetch the latest remote refs and bring the branch up to date with upstream before making task changes.
- Start new work with a clean working tree and index. If uncommitted work is clearly a completed prior task, verify and commit it first. If it is incomplete, unrelated, or its status is unclear, stop and ask the user rather than stashing, discarding, or mixing it into the new task.
- Fast-forward when possible. Integrate upstream changes only when the merge is clear and conflict-free; stop and ask if conflicts or competing changes require a decision. Do not overwrite remote history.
- Commit all completed task changes in focused commits after the appropriate checks, including documentation-only changes. Leave the tree clean when the task is done.
- Fetch again before pushing. Push completed commits when the remote can accept them without conflicts. If the upstream has diverged or a push is rejected, do not force-push; resolve a straightforward conflict-free integration or stop and ask.

## File Map Maintenance
- After adding, deleting, or renaming any source file, update `FILEMAP.md` to reflect the change.
- When refactoring moves code between files or changes a file's responsibility, update the description in `FILEMAP.md`.
- Keep `FILEMAP.md` entries concise - one to two lines per file maximum. If a file is doing more than can be reasonably construed within that limit, consider refactoring it.

## Tech Stack
- Not yet chosen. Do not pick a stack, framework, or packaging approach without agreeing it with the user first.
- The target platform is Windows.

## Architectural Overview
- Each AI service is a provider that reports named usage windows (used percent, window length, reset time) behind a common shape; the UI depends on that shape, not on provider internals.
- Usage checks are read-only and must never consume quota.
- Credentials are owned by this app. Never read, share, or refresh another application's tokens.

## Code Style
- Prefer small focused modules and explicit control flow over clever abstractions.
- Never log, display, or persist access tokens, refresh tokens, account IDs, or emails. Drop identifying fields from service responses before storing samples.
- Treat a missing usage value as unknown, never as 0%. Surface HTTP status and backend error detail when a source fails or changes shape.
- Never use native dialogs (`alert`, `confirm`, `prompt`, message boxes) for user interaction; build them into the app UI.

## Verification
- Do not add test projects or automated tests unless the user explicitly requests them.
- Verify UI changes in the running app and record which interactions were actually checked.
- Never leave an app instance, server, or watcher started for verification running.

## Conventions
- When you need to understand current wiring, start with `VISION.md`, then `FILEMAP.md`, then the relevant area.
- Record findings about undocumented service endpoints in `docs/`, dated, so changes in behavior can be traced.
- Trace each requested change through its full impact area before considering the work complete. Changes to models, contracts, or core concepts should include all affected layers such as providers, storage, UI, and documentation.
- Remove superseded code and concepts when replacing them. Do not leave deprecated components, handlers, or other logic in place just because the new path works.
- This is a local development project. When a requested change replaces a concept, remove the superseded implementation outright; do not add or retain compatibility shims, legacy fallbacks, or dual paths unless the user explicitly asks for a transition path.
