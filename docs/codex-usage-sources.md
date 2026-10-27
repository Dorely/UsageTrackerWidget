# Codex usage sources

Technical notes for tracking ChatGPT/Codex quotas, including the separate image allowance. Recorded 2026-10-24. These are undocumented backend endpoints and may change.

## Goal

Show where the quotas stand right now and how they change over time:

- the Codex 5-hour window;
- the Codex weekly window;
- the separate daily image-generation allowance, which the usage page doesn't show anywhere.

Polling should be cheap and must never consume quota.

## Auth (Codex OAuth, the same as Codex CLI and ImageGenCli)

- Every request needs `authorization: Bearer <access token>` and `chatgpt-account-id: <JWT claim "https://api.openai.com/auth".chatgpt_account_id>`.
- Also send `user-agent` and `accept: application/json`, plus an `originator` header (ImageGenCli sends `pi`).
- OAuth: PKCE against `https://auth.openai.com/oauth/authorize` and `/oauth/token`, with client id `app_EMoamEEZ73f0CkXaXp7hrann`, scope `openid profile email offline_access`, and loopback redirect `http://localhost:1455/auth/callback`. Codex CLI and ImageGenCli use that exact redirect. UsageTrackerWidget uses an ephemeral `http://127.0.0.1:<port>/auth/callback` instead because the authorization server accepts arbitrary loopback ports for this client; see the tracker notes below.
- **Use a separate login, or delegate to the app that owns the token. Never share another app's refresh token.** Refresh tokens rotate, so if two processes refresh the same token, the login gets revoked. ImageGenCli guards its own token with a cross-process refresh lock.
  - Option A (simplest): the tracker runs its own OAuth login and keeps its own token store (OS vault, with an encrypted-file fallback).
  - Option B: add an `imagegen usage` command to ImageGenCli that prints the numbers below as JSON, and have the tracker shell out to it.
- Never log, display, or persist the access token, the refresh token, the account ID, or the email. The usage response includes `user_id`, `account_id` and `email`; drop them.

## Source 1: Codex windows (free, read-only)

`GET https://chatgpt.com/backend-api/wham/usage`

Relevant fields:

```json
{
  "plan_type": "plus",
  "rate_limit": {
    "allowed": true, "limit_reached": false,
    "primary_window":   { "used_percent": 0,  "limit_window_seconds": 18000,  "reset_after_seconds": 18000,  "reset_at": 1792900350 },
    "secondary_window": { "used_percent": 17, "limit_window_seconds": 604800, "reset_after_seconds": 499774, "reset_at": 1793382124 }
  },
  "credits": { "has_credits": false, "balance": "0" },
  "rate_limit_reached_type": null
}
```

- The primary window is 5 hours and the secondary window is weekly.
- This response does **not** include the image allowance.

## Source 2: Daily image allowance (free, via a deliberately invalid request)

`POST https://chatgpt.com/backend-api/codex/images/generations` with this body:

```json
{"prompt":"x","model":"gpt-image-2.5-flare","background":"invalid"}
```

- The server rejects it with HTTP 400 (`invalid_value` on `background`) and generates nothing.
- The response headers still carry the meter:
  - `x-codex-active-limit: imagegen_premium`
  - `x-codex-primary-used-percent: 46`
  - `x-codex-primary-window-minutes: 1440` (a 24-hour window)
  - `x-codex-primary-reset-at`: present on successful image responses; check whether 400s include it
  - `x-codex-secondary-window-minutes: 0` (an unused slot)
  - `x-codex-plan-type` (it may also appear here)
- Every real image call returns the same headers, so any image client can record them for free.
- Read only these named numeric headers. Ignore other `x-codex-*` headers, which can be opaque state.

### Observations (2026-10-24, Plus account)

- A body with no prompt (`{"model":"gpt-image-2.5-flare"}`) is also rejected with HTTP 400 (`missing_required_parameter` on `prompt`) and carries the same headers.
- 400 responses do include `x-codex-primary-reset-at` (epoch seconds) and `x-codex-primary-reset-after-seconds`, along with `x-codex-plan-type`, `x-codex-primary-over-secondary-limit-percent`, and `x-codex-credits-*`.
- Several consecutive invalid requests left `x-codex-primary-used-percent` unchanged.

## Calibration data (Plus, October 2026; approximate)

- A low-quality opaque image costs about 20 input and 430–470 output image tokens.
- Each input or reference image adds about 1,480 input tokens.
- Transparent output renders at medium quality and costs about 830–1,760 tokens.
- The daily meter rose about 1% per 6–7 low-quality images.
- It is not yet known whether the meter counts tokens or images. If ImageGenCli's `IMAGEGEN_USAGE_LOG` is also enabled (one prompt-free JSONL line per image, with token usage and the used percent), the tracker can correlate the two and answer that.

## Tracker requirements

- Poll both sources on an interval, every 5–15 minutes, plus a manual refresh. Store timestamped samples locally, such as SQLite or JSONL.
- Show the current percent and the time until reset for each of the three windows, a chart of percent over time, and markers where a window reset.
- Optionally show a burn rate and a projected time until the limit.
- Support more than one account. Quotas are per account, and different logins can be on different plans (for example `prolite` and `plus`). Label each account by `plan_type` and a local nickname, never by email or ID.
- Handle 401 by refreshing once. Handle 429 or `limit_reached` by showing it, not by retrying in a loop. Treat a missing header as unknown, not as 0%.
- These are undocumented backend endpoints that can change, so fail visibly: show the HTTP status and backend error detail when the response shape changes.

## Reference implementations

- codex-rs `backend-client`: rate-limit fetch from `/wham/usage`.
- codex-rs `codex-api/src/endpoint/images.rs`: the image endpoints.
- ImageGenCli `packages/providers-codex`: OAuth, token refresh lock, `readPlanLimits(headers)`.

## Tracker implementation notes (2026-10-25)

- UsageTrackerWidget uses its own OAuth grant and DPAPI-protected token store. It does not read or refresh Codex CLI or ImageGenCli credentials.
- The authorization endpoint accepts `http://127.0.0.1:<arbitrary-port>/auth/callback` for this client. A probe of ports 1456, 18743, and 49321 all redirected to the normal OpenAI login page, while `http://localhost:1456/auth/callback` returned HTTP 400. The tracker therefore binds an ephemeral IPv4 loopback port before opening the browser and reuses that exact redirect URI for the code exchange. This avoids the fixed `localhost:1455` collision with Codex CLI, ImageGenCli, and Lorekeeper.
- The browser must not be opened until the loopback listener has bound successfully; otherwise another local `/auth/callback` host can consume the authorization redirect.
- The image-meter probe sends `{"model":"gpt-image-2.5-flare"}` with no prompt. This is deliberately incomplete so the backend cannot generate an image even if the `background` validation behavior changes.
- The tracker accepts image-meter data only from HTTP 400 responses whose body identifies the missing required prompt (`missing_required_parameter` or a `prompt` parameter error). It reads only `x-codex-primary-used-percent`, `x-codex-primary-window-minutes`, `x-codex-primary-reset-at`, `x-codex-primary-reset-after-seconds`, and `x-codex-plan-type`.
- A different image response disables automatic image-meter tracking until the user explicitly re-enables it. There is no retry loop for this probe.
- Live end-to-end verification of the signed-in image-meter response is still required with the tracker's own account login. No tokens or account-identifying values were recorded during implementation.

## Image allowance exhausted (2026-10-27, Plus account)

- The meter jumped from 70% to 92% between two 10-minute polls, and image generation was then refused while the last 400 probe still read 92%. The allowance can block before the meter reaches 100%, or a single large request can push it over.
- Once blocked, the missing-prompt probe returns **HTTP 429** instead of 400: the limit check runs before request validation. The body has `error.type: "usage_limit_reached"` (no `code` or `param`).
- The 429 still carries the meter headers: `x-codex-active-limit: imagegen_premium`, `x-codex-primary-used-percent: 100`, `x-codex-primary-window-minutes: 1440`, `x-codex-primary-reset-at`, `x-codex-primary-reset-after-seconds`, `x-codex-plan-type`, plus `x-codex-secondary-*` (0), `x-codex-primary-over-secondary-limit-percent` (0), and `x-codex-credits-*`.
- `/wham/usage` at the same moment reported `limit_reached: false` and `rate_limit_reached_type: null`; the image limit is not reflected there.
- The tracker now accepts a 429 whose `error.type` is `usage_limit_reached` as a valid meter reading and marks the daily-image window as limit reached. Other 429s remain transient errors.
