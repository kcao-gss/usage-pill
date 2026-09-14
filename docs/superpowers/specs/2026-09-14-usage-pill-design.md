# Usage Pill - Design Spec

Date: 2026-09-14
Status: Approved (design), pending implementation plan

## 1. Purpose

A minimal always-on-top Windows desktop pill that shows current AI usage limits at a
glance. MVP covers Claude subscription limits only (the same numbers as the `/usage`
command in Claude Code). The architecture leaves one thin seam for a second provider
later, and nothing more.

Success criteria:

- The pill shows the current 5-hour session utilization within 5 minutes of truth.
- The pill never blocks, never steals focus, and never crashes on bad data.
- The app never writes to Claude Code credential files.

## 2. Data source (verified)

`GET https://api.anthropic.com/api/oauth/usage`

Headers:

- `Authorization: Bearer <claudeAiOauth.accessToken>`
- `anthropic-beta: oauth-2025-04-20`

Token source: `%USERPROFILE%\.claude\.credentials.json`, JSON shape:

```json
{
  "claudeAiOauth": {
    "accessToken": "sk-ant-oat01-...",
    "refreshToken": "sk-ant-ort01-...",
    "expiresAt": 1789423372964,
    "refreshTokenExpiresAt": 1790740179964,
    "subscriptionType": "team",
    "rateLimitTier": "default_claude_max_5x"
  }
}
```

Verified live response (2026-09-14, HTTP 200), trimmed to the fields this app uses:

```json
{
  "five_hour": { "utilization": 90.0, "resets_at": "2026-09-14T19:10:00.836238+00:00" },
  "seven_day": { "utilization": 17.0, "resets_at": "2026-09-17T02:00:00.836258+00:00" },
  "limits": [
    { "kind": "session",       "group": "session", "percent": 90, "severity": "critical", "resets_at": "...", "scope": null,                          "is_active": true },
    { "kind": "weekly_all",    "group": "weekly",  "percent": 17, "severity": "normal",   "resets_at": "...", "scope": null,                          "is_active": false },
    { "kind": "weekly_scoped", "group": "weekly",  "percent": 15, "severity": "normal",   "resets_at": "...", "scope": { "model": { "display_name": "Fable" } }, "is_active": false }
  ],
  "extra_usage": { "is_enabled": true, "monthly_limit": 20000, "used_credits": 0.0, "currency": "USD", "decimal_places": 2 },
  "spend": { "used": { "amount_minor": 0, "currency": "USD", "exponent": 2 },
             "limit": { "amount_minor": 20000, "currency": "USD", "exponent": 2 },
             "percent": 0, "severity": "normal", "enabled": true }
}
```

Parsing rules:

- The `limits[]` array is the primary source, because it carries `kind`, `severity`,
  and model `scope`. The top-level `five_hour` / `seven_day` objects are the fallback
  when `limits[]` is absent or empty.
- Unknown `kind` values and unknown top-level keys (the response contains several
  code-name keys such as `nimbus_quill`, `tangelo`) MUST be ignored without error.
- All fields except `percent` MUST be treated as nullable.

Known risk: public bug reports show this endpoint returns HTTP 429 when polled often.
The client MUST poll slowly, honour `Retry-After`, and back off.

## 3. Architecture

Single .NET 8 WPF application, target `net8.0-windows`, `UseWPF` + `UseWindowsForms`
(the latter only for the tray `NotifyIcon`). Built and run with the Windows .NET SDK.

```
UsagePill.App (WPF)
├─ Core
│  ├─ IUsageProvider        // ProviderId, DisplayName, Task<UsageSnapshot> FetchAsync(CancellationToken)
│  ├─ UsageSnapshot         // immutable record: provider, limits[], spend?, capturedAt
│  ├─ UsageLimit            // kind, group, percent, severity, resetsAt?, scopeLabel?, isActive
│  └─ UsageState            // Ok | Stale | NoCredentials | AuthExpired | Error + last good snapshot
├─ Claude
│  ├─ ClaudeCredentialStore // locate + read credentials file, read-only
│  └─ ClaudeUsageProvider   // HTTP call + JSON mapping
├─ Polling
│  └─ UsagePoller           // interval timer, backoff, holds last good snapshot, raises StateChanged
├─ Settings
│  └─ AppSettings           // JSON at %APPDATA%\usage-pill\settings.json
└─ Ui
   ├─ PillWindow            // borderless, topmost, drag to move
   ├─ DetailPopup           // click target: all limits, resets, credits
   ├─ SettingsWindow        // field toggles, interval, opacity, thresholds
   └─ TrayIcon              // show/hide, settings, refresh now, quit
```

Rules:

- `Core` and `Claude` have no WPF references, so they are unit-testable.
- The UI layer never performs HTTP or file IO directly.

## 4. Data flow

1. `UsagePoller` fires (default every 5 minutes, also on demand from the tray).
2. `ClaudeCredentialStore` re-reads the credentials file on every poll. Claude Code
   refreshes the access token in place, so re-reading picks up the new token for free.
3. `ClaudeUsageProvider` sends the request and maps the response to a `UsageSnapshot`.
4. The poller publishes a `UsageState`; `PillWindow` and `DetailPopup` re-render.

The app never writes the credentials file and never performs an OAuth refresh.

## 5. Pill face

- Default face: session percentage only, for example `90%`.
- Optional face fields, each independently toggled in settings:
  - weekly-all percentage
  - time until session reset (`2h14m`)
  - per-model weekly percentage with the model name
- The pill width follows its content. The user drags the pill to any position; the
  position is saved and restored on the next start.
- Click the pill opens `DetailPopup`, which always shows every limit, every reset
  time, and extra-credit spend.

Colour rules:

- `severity: critical` from the API renders red.
- `percent >= warnThreshold` (default 75) renders amber.
- Otherwise neutral.
- A small dot in the pill marks a stale value.

## 6. Error handling

| Condition | Pill face | Tooltip | Poller behavior |
|---|---|---|---|
| HTTP 429 | last good value + stale dot | "Rate limited, retrying at HH:MM" | Honour `Retry-After`; else exponential backoff 5→10→20→40 min, cap 60 min |
| Network or 5xx error | last good value + stale dot | error summary | Exponential backoff 1→2→4 min, cap 15 min |
| Credentials file missing or unparsable | `-` | "Claude Code not logged in" | Retry at normal interval |
| `expiresAt` in the past AND request returns 401 | `!` | "Claude Code login expired - start Claude Code to refresh" | Retry at normal interval |
| No data yet at startup | `...` | "Loading" | - |

Unhandled exceptions in a poll cycle MUST be caught, logged, and surfaced as the
error state. A failed poll never terminates the poller.

## 7. Settings

`%APPDATA%\usage-pill\settings.json`:

```json
{
  "pollIntervalMinutes": 5,
  "face": { "session": true, "weekly": false, "resetTime": false, "perModelWeekly": false },
  "warnThresholdPercent": 75,
  "opacity": 0.92,
  "window": { "left": 40, "top": 40 },
  "startWithWindows": false
}
```

- Written atomically (temp file plus move).
- A corrupt file falls back to defaults and is replaced on the next save.
- `startWithWindows` toggles a shortcut in the user Startup folder. No registry writes.

## 8. Testing

Unit tests (xunit, `UsagePill.Tests`):

- Mapping the recorded real JSON response into `UsageSnapshot`, including the
  per-model `scope.model.display_name` label.
- A response with an empty `limits[]` falls back to `five_hour` / `seven_day`.
- Unknown `kind` values and unknown top-level keys do not throw.
- Backoff: `Retry-After` is honoured; repeated 429 grows the delay and stops at the cap.
- Colour selection at threshold boundaries.
- Settings round trip, and corrupt settings fall back to defaults.

End-to-end verification (manual, required before "done"):

- Build and run the real `.exe` on Windows.
- Screenshot the pill and the detail popup, and check the look pixel by pixel.
- Verify drag, position persistence, tray actions, and quit.

## 9. Non-goals for MVP

- Providers other than Claude.
- OAuth token refresh by this app.
- Usage history, charts, or notifications.
- An installer. A published folder plus an `.exe` is enough.

## 10. Open items

- The exact pill visual design is decided in a separate UI review before implementation.
