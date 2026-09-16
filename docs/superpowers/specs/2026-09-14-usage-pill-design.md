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

Token source: the Claude Code credentials file, on Windows at
`%USERPROFILE%\.claude\.credentials.json` and in each WSL distribution at
`\\wsl.localhost\<distro>\home\<user>\.claude\.credentials.json` (plus the `root`
equivalent). The file with the latest `claudeAiOauth.expiresAt` is used, except that a
token the endpoint has rejected ranks below every other readable token. JSON shape:

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

Only `claudeAiOauth.accessToken` and `claudeAiOauth.expiresAt` are read: the token for
the request, the expiry to rank the sources. The other fields are shown for context.

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
│  ├─ WslDistributions      // distribution names from the Lxss registry key
│  ├─ CredentialSources     // every candidate credentials path, Windows and WSL
│  ├─ ClaudeCredentialStore // read one credentials file, read-only
│  ├─ ClaudeCredentialResolver // choose the freshest unrejected source, remember it, rescan on failure
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
2. `ClaudeCredentialResolver` re-reads the chosen credentials file on every poll. Claude
   Code refreshes the access token in place, so re-reading picks up the new token for
   free. It scans all Windows and WSL locations again at startup, when that file stops
   being readable, and when the endpoint rejects the token. A rescan after a rejection
   ranks the rejected token last, so a second Claude Code takes over; the rejection
   follows the token string, so the same file wins again once it holds a fresh token.
3. `ClaudeUsageProvider` sends the request and maps the response to a `UsageSnapshot`.
4. The poller publishes a `UsageState`; `PillWindow` and `DetailPopup` re-render.

The app never writes the credentials file and never performs an OAuth refresh.

## 5. Pill appearance

Decided in the UI review of 2026-09-14 (`docs/superpowers/specs/2026-09-14-usage-pill-ui.html`).

### 5.1 Shape

The pill is a row of circular ring gauges inside one acrylic capsule.

- Each ring is a donut: the arc is the utilization, the remaining circle is the
  track. The arc starts at 12 o'clock and fills clockwise as usage grows.
- The percentage sits on an acrylic disc inside its own ring, as a bare number
  with no `%` sign. `100` is rendered at a reduced font size so it fits.
- Ring order, left to right: Session (5 hour), Weekly all models, Weekly per model.
- No text labels. The order plus the tooltip plus the detail card identify the rings.
- The capsule carries the drag surface and the shadow.

### 5.2 Geometry

| Property | Value |
|---|---|
| Ring diameter | `ringSizePx`, range 24-48, default 32 |
| Ring thickness | `ringSizePx * 0.094`, so 3 px at the default |
| Font size inside the ring | `ringSizePx * 0.344`, so 11 px at the default |
| Gap between rings | `ringSizePx * 0.25`, so 8 px at the default |
| Capsule padding | 5 px on the long axis, 7 px on the short axis |
| Corner radius | fully round (half the short side) |
| Total at default, 3 rings, horizontal | 126 x 42 px |
| Total at default, 1 ring | 46 x 42 px |

All values are in device-independent pixels; WPF scales them by the monitor DPI.

### 5.3 Behaviour

- Each ring is an independent switch in settings. Session cannot be switched off.
  Switching a ring off removes it and the capsule shrinks.
- Orientation is horizontal or vertical, switchable in settings. Vertical stacks
  the same rings in the same order, top to bottom.
- The window is a normal always-on-top window; it is always clickable.
- Dragging the capsule moves the pill. On release it snaps to the nearest screen
  edge or corner when the pointer is within 16 px of it. The position is saved.
- Hover shows a tooltip only. Left click opens `DetailPopup`. Right click opens
  the same menu as the tray icon.
- Light and dark follow the Windows app theme.

### 5.4 Colour

Each ring takes its colour from its own value:

- `severity: critical` from the API renders red (`#F2555A`).
- `percent >= warnThresholdPercent` (default 75) renders amber (`#F5B73D`).
- Otherwise green (`#3ECF8E`).
- No data or a disabled limit renders grey (`#6C7684`).

A stale snapshot dims the whole capsule to 60% opacity and puts a grey dot on the
top right of the first ring.

### 5.5 Optional text tail

Separate from the rings, settings can add the session reset time as text
(`RESETS 2h 14m`) inside the capsule, to the right of the rings in horizontal
mode and below them in vertical mode.

## 6. Error handling

| Condition | Rings | Tooltip | Poller behavior |
|---|---|---|---|
| HTTP 429 | last good values, capsule at 60% opacity, grey dot | "Rate limited, retrying at HH:MM" | Honour `Retry-After`, but never below a 60 second floor; without the header, exponential backoff 5→10→20→40 min, cap 60 min |
| Network or 5xx error | last good values, capsule at 60% opacity, grey dot | error summary | Exponential backoff 1→2→4→8 min, cap 15 min |
| Credentials file missing or unparsable | all rings grey and empty, `-` inside | "Claude Code not logged in" | Exponential backoff 15→30→60→120→240 s, cap at the poll interval |
| Request returns 401 or 403 | first ring amber and full with `!`, others grey | "Login expired - start Claude Code to refresh" | Same ladder as above, shared with it |
| No data yet at startup | all rings grey and empty, `--` inside | "Loading" | - |

The 429 floor exists because the endpoint is known to return `Retry-After: 0`, which
would otherwise make the poller spin against the service that just rate limited it.
Auth expiry is driven by the HTTP status alone, never by the local `expiresAt` value,
which only ranks the sources against each other. The auth ladder starts at 15 seconds
because Claude Code refreshes the token in place, usually within a minute of the pill
seeing it rejected; it never exceeds the poll interval, so a signed-out machine costs
no more requests than an ordinary poll.

Unhandled exceptions in a poll cycle MUST be caught, logged, and surfaced as the
error state. A failed poll never terminates the poller.

## 7. Settings

`%APPDATA%\usage-pill\settings.json`:

```json
{
  "pollIntervalMinutes": 5,
  "rings": { "session": true, "weeklyAll": true, "weeklyPerModel": true },
  "ringSizePx": 32,
  "orientation": "horizontal",
  "showResetTimeText": false,
  "warnThresholdPercent": 75,
  "opacity": 0.92,
  "window": { "left": 40, "top": 40 },
  "startWithWindows": false
}
```

- Written atomically (temp file plus move).
- A corrupt file falls back to defaults and is replaced on the next save.
- `startWithWindows` toggles a shortcut in the user Startup folder. No registry writes.
- `rings.session` is forced to `true` on load; the session ring cannot be switched off.
- `ringSizePx` is clamped to 24-48 on load. `orientation` accepts `horizontal` or
  `vertical`; any other value falls back to `horizontal`.

## 8. Testing

Unit tests (xunit, `UsagePill.Tests`):

- Mapping the recorded real JSON response into `UsageSnapshot`, including the
  per-model `scope.model.display_name` label.
- A response with an empty `limits[]` falls back to `five_hour` / `seven_day`.
- Unknown `kind` values and unknown top-level keys do not throw.
- Backoff: `Retry-After` is honoured; repeated 429 grows the delay and stops at the cap;
  auth and credential failures climb from 15 s to the poll interval and share one ladder.
- Credential ranking: latest expiry wins, a rejected token ranks last, a refreshed token
  in the rejected file wins again, and the only token on the machine is kept even rejected.
- Colour selection at threshold boundaries, per ring.
- Ring geometry: thickness, font size, and gap derived from `ringSizePx` at 24, 32, and 48.
- Settings clamping: out-of-range `ringSizePx`, unknown `orientation`, and
  `rings.session: false` are all corrected on load.
- Settings round trip, and corrupt settings fall back to defaults.

End-to-end verification (manual, required before "done"):

- Build and run the real `.exe` on Windows.
- Screenshot the pill and the detail popup, and check the look pixel by pixel
  against `docs/superpowers/specs/2026-09-14-usage-pill-ui.html`.
- Verify drag, edge snapping, position persistence, orientation switch, ring
  switches, the size slider, tray actions, and quit.

## 9. Non-goals for MVP

- Providers other than Claude.
- OAuth token refresh by this app.
- Usage history, charts, or notifications.
- An installer. A published folder plus an `.exe` is enough.

## 10. Resolved

All design and interface questions are settled. The implementation plan is at
`docs/superpowers/plans/2026-09-14-usage-pill.md`.
