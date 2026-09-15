# Usage Pill

A small always-on-top Windows pill that shows your Claude usage limits as ring gauges.

## What it shows

Three rings, in order: the 5 hour session window, the weekly all-model limit,
and the weekly per-model limit. The number inside each ring is the percentage used.
Each ring can be switched off individually in Settings, except the session ring,
which is always shown.

## How it gets the data

It reads the access token that Claude Code stores in
`%USERPROFILE%\.claude\.credentials.json` and calls
`https://api.anthropic.com/api/oauth/usage`, the same endpoint behind the `/usage`
command in Claude Code. The app only reads that file. It never writes to it and
never refreshes the token itself.

If the token has expired, the usage endpoint rejects it and the session ring shows
`!` in amber. The tooltip and the detail card both read "Login expired - start
Claude Code to refresh". Start Claude Code again to refresh the token; the pill
picks it up on the next poll.

## Build and run

The Windows .NET 8 SDK is required. From WSL, `build.sh` wraps the Windows SDK
so builds run against a real Windows path:

```bash
./build.sh build     # compile
./build.sh test      # run unit tests
./build.sh run       # run the app
./build.sh publish   # produce UsagePill.exe
```

`./build.sh publish` produces a framework-dependent build at
`src/UsagePill/bin/Release/net8.0-windows/win-x64/publish/UsagePill.exe`. It
still needs the Windows .NET 8 desktop runtime installed to run.

## Settings

Open from the tray icon's right-click menu ("Settings...") or edit
`%APPDATA%\usage-pill\settings.json` directly:

- Which rings to show (weekly-all and weekly-per-model; the session ring cannot
  be turned off)
- Ring size, 24-48 px
- Horizontal or vertical layout
- Show the reset time as text next to the rings
- Opacity, 30-100%
- Refresh interval, 1-60 minutes
- The amber warning threshold, 1-100%
- Start with Windows

All numeric settings are clamped to their valid range when loaded, so an
edited or corrupted `settings.json` cannot put the app in a broken state.

## Design documents

- Spec: `docs/superpowers/specs/2026-09-14-usage-pill-design.md`
- Visual reference: `docs/superpowers/specs/2026-09-14-usage-pill-ui.html`
- Implementation plan: `docs/superpowers/plans/2026-09-14-usage-pill.md`
