Usage Pill {VERSION}, an always-on-top pill showing your Claude usage limits as ring gauges.

## Which download

- **self-contained** - runs as-is, no .NET install needed. Larger, because WPF cannot be trimmed.
- **framework-dependent** - small, but needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0/runtime).

Unzip anywhere and run `UsagePill.exe`. Use the tray menu to switch on Start with Windows.

Checksums are in `SHA256SUMS.txt`.

## Requirements

- Windows 10 or 11, x64.
- Claude Code signed in on the same machine. The app reads its credentials file read-only and never refreshes the token.

See the README for the full guide.
