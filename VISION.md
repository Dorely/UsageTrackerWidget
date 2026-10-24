# UsageTrackerWidget Vision

## Product Vision

UsageTrackerWidget is a small, always-available Windows widget that shows how much of each AI service's usage limits remain. It lives where a glance is cheap, ideally on or next to the taskbar, and answers one question without opening a browser: how much do I have left, and when does it reset?

The core philosophy is:

> Make every quota visible, including the ones the services themselves don't show.

The first target is Codex, including its separate daily image-generation allowance, which no official usage page currently displays.

---

# Core Product Principles

## 1. Glanceable First

The primary surface is tiny and passive: current percent and time until reset at a glance, with detail (history, charts, burn rate) one click away. The widget must never demand attention it does not need.

## 2. Read-Only and Quota-Free

Checking usage must never consume usage. The widget observes limits; it does not spend them, retry into them, or change account state.

## 3. Multi-Service, Multi-Account

Each AI service is its own provider behind a common shape: named windows with a used percent, a window length, and a reset time. More than one account per service is supported, labelled by plan and a local nickname, never by email or account ID.

## 4. Honest About Unknowns

Many usage sources are undocumented and can change. A missing value is shown as unknown, never as 0%. When a source breaks or changes shape, the widget fails visibly with enough detail to diagnose it.

## 5. Local and Private

All data stays on the user's machine. Credentials live in OS-protected storage, are owned by this app (never borrowed from another app's token store), and never appear in logs, the UI, or stored samples. Identifying fields returned by services are dropped.

---

# MVP Scope

## Phase 1: Codex

- Current Codex 5-hour and weekly windows.
- The daily image-generation allowance.
- Percent used and time until reset for each window, with manual refresh and periodic polling.
- Locally stored, timestamped samples to show how usage changes over time.

## Beyond

- History charts with reset markers, burn rate, and projected time until a limit is reached.
- Additional services (for example Claude, Gemini, OpenRouter credits) as separate providers using the same window model.

---

# Non-Goals

- No hosted service, telemetry, or cloud sync.
- No spending, purchasing, or changing plan settings on the user's behalf.
- No scraping of browser sessions or reuse of another application's credentials.

---

# Product North Star

> A single glance at the taskbar should tell me whether I can keep working with each AI service, and for how long.
