# Usage Pill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows desktop app that shows Claude subscription usage limits as three always-on-top ring gauges plus a tray icon.

**Architecture:** One .NET 8 WPF application. A pure-C# core (`Core`, `Claude`, `Polling`, `Settings` namespaces, no WPF types) reads the Claude Code credentials file, calls the Anthropic OAuth usage endpoint on a timer, and publishes an immutable `UsageState`. The WPF layer (`Ui` namespace) renders that state and never performs HTTP or file IO.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF, WinForms `NotifyIcon` (tray only), `System.Text.Json`, xunit.

**Spec:** `docs/superpowers/specs/2026-09-14-usage-pill-design.md`
**Visual reference:** `docs/superpowers/specs/2026-09-14-usage-pill-ui.html` (open it in a browser; the built app must match it)

## Global Constraints

- Target framework: `net8.0-windows`. The Windows SDK is `C:\Program Files\dotnet\dotnet.exe` (8.0.422). The WSL `dotnet` (10.0.112) MUST NOT be used - it cannot produce the WPF desktop output this project needs.
- All build and test commands run through the Windows SDK from WSL, for example:
  `"/mnt/c/Program Files/dotnet/dotnet.exe" build`
- The repository lives at `/home/kcao/Projects/usage-pill`, which Windows sees as `\\wsl.localhost\Ubuntu\home\kcao\Projects\usage-pill`. MSBuild refuses UNC working directories, so every `dotnet.exe` invocation MUST pass an explicit project path and run with `cwd` set to a real Windows directory. Use the wrapper created in Task 1.
- `Nullable` is `enable`. `TreatWarningsAsErrors` is `true`.
- The app MUST NEVER write to any file under `%USERPROFILE%\.claude`. Reads only.
- The app MUST NEVER perform an OAuth refresh.
- No em dash characters anywhere in code, comments, or UI copy. Use a plain hyphen.
- Colours, verbatim: red `#F2555A`, amber `#F5B73D`, green `#3ECF8E`, grey `#6C7684`.
- Ring geometry, verbatim: thickness `ringSizePx * 0.094`, font size `ringSizePx * 0.344`, gap `ringSizePx * 0.25`, capsule padding 5 px long axis and 7 px short axis.
- `ringSizePx` range 24-48, default 32. `warnThresholdPercent` default 75. `pollIntervalMinutes` default 5.
- Ring order, left to right (or top to bottom): Session, Weekly all models, Weekly per model.
- Commit after every task with a conventional-commit message. Never add a co-author line.

---

## File Structure

| File | Responsibility |
|---|---|
| `build.sh` | Wrapper that runs the Windows dotnet SDK with a valid Windows working directory |
| `UsagePill.sln` | Solution holding both projects |
| `src/UsagePill/UsagePill.csproj` | WPF app project |
| `src/UsagePill/Core/UsageLimit.cs` | `LimitKind`, `Severity`, `UsageLimit` record |
| `src/UsagePill/Core/UsageSnapshot.cs` | `UsageSnapshot` record and its lookup helpers |
| `src/UsagePill/Core/UsageState.cs` | `UsageStatus` enum and `UsageState` record |
| `src/UsagePill/Core/IUsageProvider.cs` | Provider seam |
| `src/UsagePill/Core/UsageException.cs` | `AuthExpiredException`, `NoCredentialsException`, `RateLimitedException` |
| `src/UsagePill/Claude/ClaudeCredentials.cs` | Credential record |
| `src/UsagePill/Claude/ClaudeCredentialStore.cs` | Locates and reads `.credentials.json`, read-only |
| `src/UsagePill/Claude/ClaudeUsageJson.cs` | Maps the raw endpoint JSON to `UsageSnapshot` |
| `src/UsagePill/Claude/ClaudeUsageProvider.cs` | HTTP call plus mapping |
| `src/UsagePill/Polling/BackoffPolicy.cs` | Delay after success, 429, and transient errors |
| `src/UsagePill/Polling/UsagePoller.cs` | Timer loop, holds the last good snapshot, raises `StateChanged` |
| `src/UsagePill/Settings/AppSettings.cs` | Settings record with clamping |
| `src/UsagePill/Settings/SettingsStore.cs` | Atomic load and save under `%APPDATA%` |
| `src/UsagePill/Ui/RingGeometry.cs` | Pure geometry and colour maths shared by the pill and the tray icon |
| `src/UsagePill/Ui/RingGauge.cs` | Custom `FrameworkElement` that draws one ring plus its number |
| `src/UsagePill/Ui/PillWindow.xaml(.cs)` | The capsule window: rings, drag, edge snap, tooltip, click |
| `src/UsagePill/Ui/DetailPopup.xaml(.cs)` | The detail card |
| `src/UsagePill/Ui/SettingsWindow.xaml(.cs)` | Settings UI |
| `src/UsagePill/Ui/TrayIcon.cs` | `NotifyIcon`, generated icon bitmap, context menu |
| `src/UsagePill/Ui/ThemeWatcher.cs` | Reads and watches the Windows light/dark app theme |
| `src/UsagePill/App.xaml(.cs)` | Composition root and lifetime |
| `tests/UsagePill.Tests/UsagePill.Tests.csproj` | xunit test project |
| `tests/UsagePill.Tests/Fixtures/usage-response.json` | The recorded real endpoint response |
| `tests/UsagePill.Tests/*Tests.cs` | One test file per unit under test |

---

## Task 1: Repository scaffold and the Windows build wrapper

**Files:**
- Create: `build.sh`
- Create: `UsagePill.sln`
- Create: `src/UsagePill/UsagePill.csproj`
- Create: `src/UsagePill/App.xaml`, `src/UsagePill/App.xaml.cs`
- Create: `tests/UsagePill.Tests/UsagePill.Tests.csproj`
- Create: `tests/UsagePill.Tests/SmokeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `./build.sh build`, `./build.sh test`, `./build.sh run` used by every later task.

- [ ] **Step 1: Create the build wrapper**

MSBuild cannot run with a UNC current directory, so the wrapper sets `cwd` to a Windows path and passes absolute project paths converted with `wslpath -w`.

```bash
#!/usr/bin/env bash
# Runs the Windows .NET SDK against this repository from WSL.
set -euo pipefail

DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SLN_WIN="$(wslpath -w "$REPO/UsagePill.sln")"
APP_WIN="$(wslpath -w "$REPO/src/UsagePill/UsagePill.csproj")"
TEST_WIN="$(wslpath -w "$REPO/tests/UsagePill.Tests/UsagePill.Tests.csproj")"

# MSBuild refuses a UNC working directory, so run from a real Windows path.
cd /mnt/c/Windows/Temp

cmd="${1:-build}"
shift || true

case "$cmd" in
  build)   "$DOTNET" build   "$SLN_WIN"  --nologo "$@" ;;
  test)    "$DOTNET" test    "$TEST_WIN" --nologo "$@" ;;
  run)     "$DOTNET" run     --project "$APP_WIN" "$@" ;;
  publish) "$DOTNET" publish "$APP_WIN" -c Release -r win-x64 --self-contained false "$@" ;;
  *) echo "usage: build.sh [build|test|run|publish] [extra dotnet args]" >&2; exit 2 ;;
esac
```

Then `chmod +x build.sh`.

- [ ] **Step 2: Create the app project**

`src/UsagePill/UsagePill.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RootNamespace>UsagePill</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
</Project>
```

`src/UsagePill/app.manifest` (per-monitor DPI awareness, so the ring geometry stays crisp):

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

`src/UsagePill/App.xaml`:

```xml
<Application x:Class="UsagePill.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown" />
```

`src/UsagePill/App.xaml.cs`:

```csharp
using System.Windows;

namespace UsagePill;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Composition happens in Task 12.
    }
}
```

- [ ] **Step 3: Create the test project**

`tests/UsagePill.Tests/UsagePill.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\UsagePill\UsagePill.csproj" />
  </ItemGroup>
</Project>
```

`tests/UsagePill.Tests/SmokeTests.cs`:

```csharp
namespace UsagePill.Tests;

public class SmokeTests
{
    [Fact]
    public void TestProjectReferencesTheApp()
    {
        Assert.Equal("UsagePill", typeof(UsagePill.App).Assembly.GetName().Name);
    }
}
```

- [ ] **Step 4: Create the solution**

```bash
cd /mnt/c/Windows/Temp
DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
REPO=/home/kcao/Projects/usage-pill
"$DOTNET" new sln -o "$(wslpath -w $REPO)" -n UsagePill --force
"$DOTNET" sln "$(wslpath -w $REPO/UsagePill.sln)" add "$(wslpath -w $REPO/src/UsagePill/UsagePill.csproj)" "$(wslpath -w $REPO/tests/UsagePill.Tests/UsagePill.Tests.csproj)"
```

- [ ] **Step 5: Run the test to verify the toolchain**

Run: `./build.sh test`
Expected: PASS, 1 test.

- [ ] **Step 6: Commit**

```bash
git add build.sh UsagePill.sln src tests
git commit -m "chore: scaffold WPF app and test projects"
```

---

## Task 2: Core model types

**Files:**
- Create: `src/UsagePill/Core/UsageLimit.cs`
- Create: `src/UsagePill/Core/UsageSnapshot.cs`
- Create: `src/UsagePill/Core/UsageState.cs`
- Create: `src/UsagePill/Core/UsageException.cs`
- Create: `src/UsagePill/Core/IUsageProvider.cs`
- Test: `tests/UsagePill.Tests/UsageSnapshotTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum LimitKind { Session, WeeklyAll, WeeklyScoped, Unknown }`
  - `enum Severity { Normal, Warning, Critical }`
  - `record UsageLimit(LimitKind Kind, double Percent, Severity ApiSeverity, DateTimeOffset? ResetsAt, string? ScopeLabel)`
  - `record SpendInfo(decimal Used, decimal Limit, string Currency)`
  - `record UsageSnapshot(IReadOnlyList<UsageLimit> Limits, SpendInfo? Spend, DateTimeOffset CapturedAt)` with `UsageLimit? Find(LimitKind kind)`
  - `enum UsageStatus { Loading, Ok, Stale, NoCredentials, AuthExpired }`
  - `record UsageState(UsageStatus Status, UsageSnapshot? Snapshot, string? Message, DateTimeOffset? RetryAt)`
  - `interface IUsageProvider { string ProviderId { get; } Task<UsageSnapshot> FetchAsync(CancellationToken ct); }`
  - `NoCredentialsException`, `AuthExpiredException`, `RateLimitedException(TimeSpan? RetryAfter)`

- [ ] **Step 1: Write the failing test**

`tests/UsagePill.Tests/UsageSnapshotTests.cs`:

```csharp
using UsagePill.Core;

namespace UsagePill.Tests;

public class UsageSnapshotTests
{
    private static UsageLimit Limit(LimitKind kind, double percent) =>
        new(kind, percent, Severity.Normal, null, null);

    [Fact]
    public void FindReturnsTheLimitOfThatKind()
    {
        var snapshot = new UsageSnapshot(
            new[] { Limit(LimitKind.Session, 90), Limit(LimitKind.WeeklyAll, 17) },
            null,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(17, snapshot.Find(LimitKind.WeeklyAll)!.Percent);
    }

    [Fact]
    public void FindReturnsNullWhenTheKindIsAbsent()
    {
        var snapshot = new UsageSnapshot(new[] { Limit(LimitKind.Session, 90) }, null, DateTimeOffset.UnixEpoch);

        Assert.Null(snapshot.Find(LimitKind.WeeklyScoped));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `./build.sh test`
Expected: build error, `UsagePill.Core` does not exist.

- [ ] **Step 3: Write the implementation**

`src/UsagePill/Core/UsageLimit.cs`:

```csharp
namespace UsagePill.Core;

public enum LimitKind { Session, WeeklyAll, WeeklyScoped, Unknown }

public enum Severity { Normal, Warning, Critical }

/// <summary>One metered limit, for example the 5 hour session window.</summary>
public sealed record UsageLimit(
    LimitKind Kind,
    double Percent,
    Severity ApiSeverity,
    DateTimeOffset? ResetsAt,
    string? ScopeLabel);
```

`src/UsagePill/Core/UsageSnapshot.cs`:

```csharp
namespace UsagePill.Core;

public sealed record SpendInfo(decimal Used, decimal Limit, string Currency);

/// <summary>One successful read of a provider's usage endpoint.</summary>
public sealed record UsageSnapshot(
    IReadOnlyList<UsageLimit> Limits,
    SpendInfo? Spend,
    DateTimeOffset CapturedAt)
{
    public UsageLimit? Find(LimitKind kind)
    {
        for (var i = 0; i < Limits.Count; i++)
        {
            if (Limits[i].Kind == kind) return Limits[i];
        }
        return null;
    }
}
```

`src/UsagePill/Core/UsageState.cs`:

```csharp
namespace UsagePill.Core;

public enum UsageStatus { Loading, Ok, Stale, NoCredentials, AuthExpired }

/// <summary>What the UI renders. Snapshot is the last good data, even when stale.</summary>
public sealed record UsageState(
    UsageStatus Status,
    UsageSnapshot? Snapshot,
    string? Message,
    DateTimeOffset? RetryAt)
{
    public static readonly UsageState Loading = new(UsageStatus.Loading, null, null, null);
}
```

`src/UsagePill/Core/UsageException.cs`:

```csharp
namespace UsagePill.Core;

public sealed class NoCredentialsException : Exception
{
    public NoCredentialsException(string message) : base(message) { }
}

public sealed class AuthExpiredException : Exception
{
    public AuthExpiredException(string message) : base(message) { }
}

public sealed class RateLimitedException : Exception
{
    public RateLimitedException(TimeSpan? retryAfter)
        : base("The usage endpoint rate limited the request.")
        => RetryAfter = retryAfter;

    public TimeSpan? RetryAfter { get; }
}
```

`src/UsagePill/Core/IUsageProvider.cs`:

```csharp
namespace UsagePill.Core;

/// <summary>One AI provider that can report usage limits.</summary>
public interface IUsageProvider
{
    string ProviderId { get; }

    string DisplayName { get; }

    Task<UsageSnapshot> FetchAsync(CancellationToken ct);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `./build.sh test`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UsagePill/Core tests/UsagePill.Tests/UsageSnapshotTests.cs
git commit -m "feat: add core usage model types"
```

---

## Task 3: Claude response mapping

**Files:**
- Create: `src/UsagePill/Claude/ClaudeUsageJson.cs`
- Create: `tests/UsagePill.Tests/Fixtures/usage-response.json`
- Test: `tests/UsagePill.Tests/ClaudeUsageJsonTests.cs`
- Modify: `tests/UsagePill.Tests/UsagePill.Tests.csproj` (copy fixtures to output)

**Interfaces:**
- Consumes: `UsageSnapshot`, `UsageLimit`, `LimitKind`, `Severity`, `SpendInfo` from Task 2.
- Produces: `static UsageSnapshot ClaudeUsageJson.Parse(string json, DateTimeOffset capturedAt)`.

Mapping rules, from spec section 2:
- Prefer the `limits[]` array. `kind` maps: `session` to `Session`, `weekly_all` to `WeeklyAll`, `weekly_scoped` to `WeeklyScoped`, anything else to `Unknown` and it is dropped.
- `severity`: `critical` to `Critical`, `warning` to `Warning`, anything else to `Normal`.
- `scope.model.display_name` becomes `ScopeLabel`.
- When `limits[]` is missing or empty, fall back to the top-level `five_hour` and `seven_day` objects with `Severity.Normal`.
- `spend.used.amount_minor` and `spend.limit.amount_minor` are minor units; divide by `10^exponent`.
- Unknown top-level keys are ignored.

- [ ] **Step 1: Save the fixture**

Save the verbatim recorded response to `tests/UsagePill.Tests/Fixtures/usage-response.json`. Copy it from spec section 2 and keep the unknown code-name keys, because the test proves they are ignored:

```json
{
  "five_hour": { "utilization": 90.0, "resets_at": "2026-09-14T19:10:00.836238+00:00", "limit_dollars": null },
  "seven_day": { "utilization": 17.0, "resets_at": "2026-09-17T02:00:00.836258+00:00", "limit_dollars": null },
  "seven_day_opus": null,
  "nimbus_quill": { "utilization": 0.0, "resets_at": null },
  "tangelo": null,
  "extra_usage": { "is_enabled": true, "monthly_limit": 20000, "used_credits": 0.0, "currency": "USD", "decimal_places": 2 },
  "limits": [
    { "kind": "session", "group": "session", "percent": 90, "severity": "critical", "resets_at": "2026-09-14T19:10:00.836238+00:00", "scope": null, "is_active": true },
    { "kind": "weekly_all", "group": "weekly", "percent": 17, "severity": "normal", "resets_at": "2026-09-17T02:00:00.836258+00:00", "scope": null, "is_active": false },
    { "kind": "weekly_scoped", "group": "weekly", "percent": 15, "severity": "normal", "resets_at": "2026-09-17T02:00:00.836448+00:00", "scope": { "model": { "id": null, "display_name": "Fable" }, "surface": null }, "is_active": false },
    { "kind": "future_unknown_kind", "group": "weekly", "percent": 42, "severity": "normal", "resets_at": null, "scope": null, "is_active": false }
  ],
  "spend": {
    "used": { "amount_minor": 0, "currency": "USD", "exponent": 2 },
    "limit": { "amount_minor": 20000, "currency": "USD", "exponent": 2 },
    "percent": 0, "severity": "normal", "enabled": true
  },
  "member_dashboard_available": false
}
```

Add to `tests/UsagePill.Tests/UsagePill.Tests.csproj` inside a new `ItemGroup`:

```xml
  <ItemGroup>
    <None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing tests**

`tests/UsagePill.Tests/ClaudeUsageJsonTests.cs`:

```csharp
using UsagePill.Claude;
using UsagePill.Core;

namespace UsagePill.Tests;

public class ClaudeUsageJsonTests
{
    private static readonly DateTimeOffset Captured = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "usage-response.json"));

    [Fact]
    public void ReadsTheSessionLimitFromTheLimitsArray()
    {
        var snapshot = ClaudeUsageJson.Parse(Fixture(), Captured);

        var session = snapshot.Find(LimitKind.Session)!;
        Assert.Equal(90, session.Percent);
        Assert.Equal(Severity.Critical, session.ApiSeverity);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 19, 10, 0, TimeSpan.Zero), session.ResetsAt!.Value.ToUniversalTime().AddTicks(-session.ResetsAt!.Value.Ticks % TimeSpan.TicksPerSecond));
    }

    [Fact]
    public void ReadsThePerModelScopeLabel()
    {
        var snapshot = ClaudeUsageJson.Parse(Fixture(), Captured);

        Assert.Equal("Fable", snapshot.Find(LimitKind.WeeklyScoped)!.ScopeLabel);
    }

    [Fact]
    public void DropsLimitsWithAnUnknownKind()
    {
        var snapshot = ClaudeUsageJson.Parse(Fixture(), Captured);

        Assert.DoesNotContain(snapshot.Limits, l => l.Percent == 42);
        Assert.Equal(3, snapshot.Limits.Count);
    }

    [Fact]
    public void ReadsSpendInMajorUnits()
    {
        var snapshot = ClaudeUsageJson.Parse(Fixture(), Captured);

        Assert.Equal(0m, snapshot.Spend!.Used);
        Assert.Equal(200m, snapshot.Spend!.Limit);
        Assert.Equal("USD", snapshot.Spend!.Currency);
    }

    [Fact]
    public void FallsBackToFiveHourAndSevenDayWhenLimitsIsEmpty()
    {
        const string json = """
        {
          "five_hour": { "utilization": 55.0, "resets_at": "2026-09-14T19:10:00+00:00" },
          "seven_day": { "utilization": 12.0, "resets_at": "2026-09-17T02:00:00+00:00" },
          "limits": []
        }
        """;

        var snapshot = ClaudeUsageJson.Parse(json, Captured);

        Assert.Equal(55, snapshot.Find(LimitKind.Session)!.Percent);
        Assert.Equal(12, snapshot.Find(LimitKind.WeeklyAll)!.Percent);
    }

    [Fact]
    public void AnEmptyObjectProducesNoLimitsAndDoesNotThrow()
    {
        var snapshot = ClaudeUsageJson.Parse("{}", Captured);

        Assert.Empty(snapshot.Limits);
        Assert.Null(snapshot.Spend);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `./build.sh test`
Expected: build error, `ClaudeUsageJson` does not exist.

- [ ] **Step 4: Write the implementation**

`src/UsagePill/Claude/ClaudeUsageJson.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using UsagePill.Core;

namespace UsagePill.Claude;

/// <summary>Maps the /api/oauth/usage response onto the core model.</summary>
public static class ClaudeUsageJson
{
    public static UsageSnapshot Parse(string json, DateTimeOffset capturedAt)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var limits = ReadLimitsArray(root);
        if (limits.Count == 0) limits = ReadLegacyShape(root);

        return new UsageSnapshot(limits, ReadSpend(root), capturedAt);
    }

    private static List<UsageLimit> ReadLimitsArray(JsonElement root)
    {
        var result = new List<UsageLimit>(4);
        if (!root.TryGetProperty("limits", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var kind = ParseKind(GetString(item, "kind"));
            if (kind == LimitKind.Unknown) continue;

            result.Add(new UsageLimit(
                kind,
                GetDouble(item, "percent") ?? 0,
                ParseSeverity(GetString(item, "severity")),
                GetTimestamp(item, "resets_at"),
                ReadScopeLabel(item)));
        }

        return result;
    }

    private static List<UsageLimit> ReadLegacyShape(JsonElement root)
    {
        var result = new List<UsageLimit>(2);
        AddLegacy(result, root, "five_hour", LimitKind.Session);
        AddLegacy(result, root, "seven_day", LimitKind.WeeklyAll);
        return result;
    }

    private static void AddLegacy(List<UsageLimit> into, JsonElement root, string property, LimitKind kind)
    {
        if (!root.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Object) return;

        var utilization = GetDouble(node, "utilization");
        if (utilization is null) return;

        into.Add(new UsageLimit(kind, utilization.Value, Severity.Normal, GetTimestamp(node, "resets_at"), null));
    }

    private static SpendInfo? ReadSpend(JsonElement root)
    {
        if (!root.TryGetProperty("spend", out var spend) || spend.ValueKind != JsonValueKind.Object) return null;

        var used = ReadMoney(spend, "used");
        var limit = ReadMoney(spend, "limit");
        if (used is null || limit is null) return null;

        var currency = spend.TryGetProperty("limit", out var l) ? GetString(l, "currency") : null;
        return new SpendInfo(used.Value, limit.Value, currency ?? "USD");
    }

    private static decimal? ReadMoney(JsonElement spend, string property)
    {
        if (!spend.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Object) return null;
        if (!node.TryGetProperty("amount_minor", out var minor) || !minor.TryGetDecimal(out var amount)) return null;

        var exponent = 2;
        if (node.TryGetProperty("exponent", out var e) && e.TryGetInt32(out var parsed)) exponent = parsed;

        var divisor = 1m;
        for (var i = 0; i < exponent; i++) divisor *= 10m;
        return amount / divisor;
    }

    private static string? ReadScopeLabel(JsonElement limit)
    {
        if (!limit.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object) return null;
        if (!scope.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.Object) return null;
        return GetString(model, "display_name");
    }

    private static LimitKind ParseKind(string? kind) => kind switch
    {
        "session" => LimitKind.Session,
        "weekly_all" => LimitKind.WeeklyAll,
        "weekly_scoped" => LimitKind.WeeklyScoped,
        _ => LimitKind.Unknown,
    };

    private static Severity ParseSeverity(string? severity) => severity switch
    {
        "critical" => Severity.Critical,
        "warning" => Severity.Warning,
        _ => Severity.Normal,
    };

    private static string? GetString(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? GetDouble(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static DateTimeOffset? GetTimestamp(JsonElement node, string property)
    {
        var raw = GetString(node, property);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `./build.sh test`
Expected: PASS, 9 tests.

- [ ] **Step 6: Commit**

```bash
git add src/UsagePill/Claude tests/UsagePill.Tests
git commit -m "feat: map the Claude usage response onto the core model"
```

---

## Task 4: Credential store

**Files:**
- Create: `src/UsagePill/Claude/ClaudeCredentials.cs`
- Create: `src/UsagePill/Claude/ClaudeCredentialStore.cs`
- Test: `tests/UsagePill.Tests/ClaudeCredentialStoreTests.cs`

**Interfaces:**
- Consumes: `NoCredentialsException` from Task 2.
- Produces:
  - `record ClaudeCredentials(string AccessToken, DateTimeOffset ExpiresAt, string? SubscriptionType)`
  - `class ClaudeCredentialStore(string path)` with `static string DefaultPath()` and `ClaudeCredentials Read()`.
  - `Read()` throws `NoCredentialsException` when the file is missing, unreadable, not JSON, or has no `claudeAiOauth.accessToken`.

- [ ] **Step 1: Write the failing tests**

`tests/UsagePill.Tests/ClaudeCredentialStoreTests.cs`:

```csharp
using UsagePill.Claude;
using UsagePill.Core;

namespace UsagePill.Tests;

public class ClaudeCredentialStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("usagepill").FullName;

    private string WriteFile(string content)
    {
        var path = Path.Combine(_dir, ".credentials.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ReadsTheAccessTokenAndExpiry()
    {
        var path = WriteFile("""
        { "mcpOAuth": {}, "claudeAiOauth": {
            "accessToken": "sk-ant-oat01-abc", "refreshToken": "sk-ant-ort01-def",
            "expiresAt": 1789423372964, "subscriptionType": "team" } }
        """);

        var credentials = new ClaudeCredentialStore(path).Read();

        Assert.Equal("sk-ant-oat01-abc", credentials.AccessToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1789423372964), credentials.ExpiresAt);
        Assert.Equal("team", credentials.SubscriptionType);
    }

    [Fact]
    public void ThrowsNoCredentialsWhenTheFileIsMissing()
    {
        var store = new ClaudeCredentialStore(Path.Combine(_dir, "absent.json"));

        Assert.Throws<NoCredentialsException>(() => store.Read());
    }

    [Fact]
    public void ThrowsNoCredentialsWhenTheTokenIsAbsent()
    {
        var store = new ClaudeCredentialStore(WriteFile("""{ "claudeAiOauth": { "expiresAt": 1 } }"""));

        Assert.Throws<NoCredentialsException>(() => store.Read());
    }

    [Fact]
    public void ThrowsNoCredentialsWhenTheFileIsNotJson()
    {
        var store = new ClaudeCredentialStore(WriteFile("not json at all"));

        Assert.Throws<NoCredentialsException>(() => store.Read());
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `./build.sh test`
Expected: build error, `ClaudeCredentialStore` does not exist.

- [ ] **Step 3: Write the implementation**

`src/UsagePill/Claude/ClaudeCredentials.cs`:

```csharp
namespace UsagePill.Claude;

public sealed record ClaudeCredentials(string AccessToken, DateTimeOffset ExpiresAt, string? SubscriptionType);
```

`src/UsagePill/Claude/ClaudeCredentialStore.cs`:

```csharp
using System.IO;
using System.Text.Json;
using UsagePill.Core;

namespace UsagePill.Claude;

/// <summary>
/// Reads the Claude Code credentials file. This type NEVER writes: Claude Code owns
/// the file and refreshes the token in place.
/// </summary>
public sealed class ClaudeCredentialStore
{
    private readonly string _path;

    public ClaudeCredentialStore(string path) => _path = path;

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        ".credentials.json");

    public ClaudeCredentials Read()
    {
        string content;
        try
        {
            content = File.ReadAllText(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new NoCredentialsException($"Cannot read {_path}: {e.Message}");
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) ||
                oauth.ValueKind != JsonValueKind.Object ||
                !oauth.TryGetProperty("accessToken", out var token) ||
                token.ValueKind != JsonValueKind.String)
            {
                throw new NoCredentialsException("No claudeAiOauth.accessToken in the credentials file.");
            }

            var expiresAt = oauth.TryGetProperty("expiresAt", out var e) && e.TryGetInt64(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : DateTimeOffset.MinValue;

            var subscription = oauth.TryGetProperty("subscriptionType", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString()
                : null;

            return new ClaudeCredentials(token.GetString()!, expiresAt, subscription);
        }
        catch (JsonException e)
        {
            throw new NoCredentialsException($"The credentials file is not valid JSON: {e.Message}");
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `./build.sh test`
Expected: PASS, 13 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UsagePill/Claude tests/UsagePill.Tests/ClaudeCredentialStoreTests.cs
git commit -m "feat: read the Claude Code credentials file"
```

---

## Task 5: Claude usage provider

**Files:**
- Create: `src/UsagePill/Claude/ClaudeUsageProvider.cs`
- Test: `tests/UsagePill.Tests/ClaudeUsageProviderTests.cs`

**Interfaces:**
- Consumes: `IUsageProvider`, the exception types, `ClaudeCredentialStore`, `ClaudeUsageJson`.
- Produces: `class ClaudeUsageProvider(ClaudeCredentialStore store, HttpClient http, TimeProvider clock) : IUsageProvider` with `ProviderId == "claude"`, `DisplayName == "Claude"`, and `const string UsageUrl = "https://api.anthropic.com/api/oauth/usage"`.

Behaviour:
- Reads the credentials on every call, so a token Claude Code refreshed is picked up.
- Sends `Authorization: Bearer <token>` and `anthropic-beta: oauth-2025-04-20`.
- 401 or 403 throws `AuthExpiredException`.
- 429 throws `RateLimitedException` carrying `Retry-After` when the header is present.
- Any other non-success status throws `HttpRequestException`.

- [ ] **Step 1: Write the failing tests**

`tests/UsagePill.Tests/ClaudeUsageProviderTests.cs`:

```csharp
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using UsagePill.Claude;
using UsagePill.Core;

namespace UsagePill.Tests;

public class ClaudeUsageProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("usagepill").FullName;

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }

    private ClaudeCredentialStore Store()
    {
        var path = Path.Combine(_dir, ".credentials.json");
        File.WriteAllText(path, """{ "claudeAiOauth": { "accessToken": "tok-123", "expiresAt": 1789423372964 } }""");
        return new ClaudeCredentialStore(path);
    }

    private static ClaudeUsageProvider Provider(ClaudeCredentialStore store, StubHandler handler) =>
        new(store, new HttpClient(handler), TimeProvider.System);

    [Fact]
    public async Task SendsTheBearerTokenAndTheBetaHeader()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "limits": [ { "kind": "session", "percent": 90, "severity": "critical" } ] }"""),
        });

        var snapshot = await Provider(Store(), handler).FetchAsync(CancellationToken.None);

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("tok-123", handler.LastRequest!.Headers.Authorization!.Parameter);
        Assert.Equal("oauth-2025-04-20", handler.LastRequest!.Headers.GetValues("anthropic-beta").Single());
        Assert.Equal(90, snapshot.Find(LimitKind.Session)!.Percent);
    }

    [Fact]
    public async Task UnauthorizedBecomesAuthExpired()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<AuthExpiredException>(
            () => Provider(Store(), handler).FetchAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TooManyRequestsCarriesRetryAfter()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return response;
        });

        var error = await Assert.ThrowsAsync<RateLimitedException>(
            () => Provider(Store(), handler).FetchAsync(CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(90), error.RetryAfter);
    }

    [Fact]
    public async Task ServerErrorBecomesHttpRequestException()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Provider(Store(), handler).FetchAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MissingCredentialsPropagate()
    {
        var store = new ClaudeCredentialStore(Path.Combine(_dir, "absent.json"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAsync<NoCredentialsException>(
            () => Provider(store, handler).FetchAsync(CancellationToken.None));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `./build.sh test`
Expected: build error, `ClaudeUsageProvider` does not exist.

- [ ] **Step 3: Write the implementation**

`src/UsagePill/Claude/ClaudeUsageProvider.cs`:

```csharp
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using UsagePill.Core;

namespace UsagePill.Claude;

/// <summary>Reads Claude subscription limits from the OAuth usage endpoint.</summary>
public sealed class ClaudeUsageProvider : IUsageProvider
{
    public const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string BetaHeader = "oauth-2025-04-20";

    private readonly ClaudeCredentialStore _store;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;

    public ClaudeUsageProvider(ClaudeCredentialStore store, HttpClient http, TimeProvider clock)
    {
        _store = store;
        _http = http;
        _clock = clock;
    }

    public string ProviderId => "claude";

    public string DisplayName => "Claude";

    public async Task<UsageSnapshot> FetchAsync(CancellationToken ct)
    {
        // Re-read every poll: Claude Code refreshes the token in place.
        var credentials = _store.Read();

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.Add("anthropic-beta", BetaHeader);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                throw new AuthExpiredException("The usage endpoint rejected the access token.");
            case HttpStatusCode.TooManyRequests:
                throw new RateLimitedException(ReadRetryAfter(response));
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ClaudeUsageJson.Parse(json, _clock.GetUtcNow());
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return null;
        if (header.Delta is { } delta) return delta;
        if (header.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `./build.sh test`
Expected: PASS, 18 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UsagePill/Claude tests/UsagePill.Tests/ClaudeUsageProviderTests.cs
git commit -m "feat: fetch Claude usage from the OAuth endpoint"
```

---

## Task 6: Backoff policy

**Files:**
- Create: `src/UsagePill/Polling/BackoffPolicy.cs`
- Test: `tests/UsagePill.Tests/BackoffPolicyTests.cs`

**Interfaces:**
- Consumes: `RateLimitedException` from Task 2.
- Produces: `class BackoffPolicy(TimeSpan normalInterval)` with `TimeSpan NextDelay(Exception? failure)` and `void Reset()`.

Behaviour, from spec section 6:
- After success, `Reset()` is called and the next delay is the normal interval.
- 429 with `Retry-After` uses that value exactly.
- 429 without `Retry-After`: 5, 10, 20, 40 minutes, then capped at 60 minutes.
- Any other failure: 1, 2, 4 minutes, then capped at 15 minutes.
- The two ladders are independent: a 429 after transient errors starts the 429 ladder at 5 minutes.

- [ ] **Step 1: Write the failing tests**

`tests/UsagePill.Tests/BackoffPolicyTests.cs`:

```csharp
using System.Net.Http;
using UsagePill.Core;
using UsagePill.Polling;

namespace UsagePill.Tests;

public class BackoffPolicyTests
{
    private static BackoffPolicy Policy() => new(TimeSpan.FromMinutes(5));

    [Fact]
    public void SuccessUsesTheNormalInterval()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), Policy().NextDelay(null));
    }

    [Fact]
    public void RetryAfterIsHonouredExactly()
    {
        var policy = Policy();

        Assert.Equal(TimeSpan.FromSeconds(90), policy.NextDelay(new RateLimitedException(TimeSpan.FromSeconds(90))));
    }

    [Fact]
    public void RateLimitWithoutRetryAfterClimbsAndCapsAtSixtyMinutes()
    {
        var policy = Policy();
        var delays = new List<TimeSpan>();
        for (var i = 0; i < 6; i++) delays.Add(policy.NextDelay(new RateLimitedException(null)));

        Assert.Equal(
            new[] { 5, 10, 20, 40, 60, 60 }.Select(m => TimeSpan.FromMinutes(m)).ToArray(),
            delays.ToArray());
    }

    [Fact]
    public void TransientErrorsClimbAndCapAtFifteenMinutes()
    {
        var policy = Policy();
        var delays = new List<TimeSpan>();
        for (var i = 0; i < 5; i++) delays.Add(policy.NextDelay(new HttpRequestException("boom")));

        Assert.Equal(
            new[] { 1, 2, 4, 8, 15 }.Select(m => TimeSpan.FromMinutes(m)).ToArray(),
            delays.ToArray());
    }

    [Fact]
    public void SuccessResetsBothLadders()
    {
        var policy = Policy();
        policy.NextDelay(new RateLimitedException(null));
        policy.NextDelay(new RateLimitedException(null));

        Assert.Equal(TimeSpan.FromMinutes(5), policy.NextDelay(null));
        Assert.Equal(TimeSpan.FromMinutes(5), policy.NextDelay(new RateLimitedException(null)));
    }

    [Fact]
    public void AuthAndCredentialFailuresUseTheNormalInterval()
    {
        var policy = Policy();

        Assert.Equal(TimeSpan.FromMinutes(5), policy.NextDelay(new AuthExpiredException("x")));
        Assert.Equal(TimeSpan.FromMinutes(5), policy.NextDelay(new NoCredentialsException("x")));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `./build.sh test`
Expected: build error, `BackoffPolicy` does not exist.

- [ ] **Step 3: Write the implementation**

`src/UsagePill/Polling/BackoffPolicy.cs`:

```csharp
using UsagePill.Core;

namespace UsagePill.Polling;

/// <summary>
/// Decides how long to wait before the next poll. The usage endpoint rate limits
/// aggressively, so the 429 ladder is much longer than the transient one.
/// </summary>
public sealed class BackoffPolicy
{
    private static readonly TimeSpan RateLimitFirst = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RateLimitCap = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan TransientFirst = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TransientCap = TimeSpan.FromMinutes(15);

    private readonly TimeSpan _normalInterval;
    private int _rateLimitStep;
    private int _transientStep;

    public BackoffPolicy(TimeSpan normalInterval) => _normalInterval = normalInterval;

    public TimeSpan NextDelay(Exception? failure)
    {
        switch (failure)
        {
            case null:
            case AuthExpiredException:
            case NoCredentialsException:
                Reset();
                return _normalInterval;

            case RateLimitedException rateLimited:
                _transientStep = 0;
                if (rateLimited.RetryAfter is { } retryAfter) return retryAfter;
                return Climb(RateLimitFirst, RateLimitCap, ref _rateLimitStep);

            default:
                _rateLimitStep = 0;
                return Climb(TransientFirst, TransientCap, ref _transientStep);
        }
    }

    public void Reset()
    {
        _rateLimitStep = 0;
        _transientStep = 0;
    }

    private static TimeSpan Climb(TimeSpan first, TimeSpan cap, ref int step)
    {
        var doublings = step;
        step++;

        var ticks = first.Ticks;
        for (var i = 0; i < doublings && ticks < cap.Ticks; i++) ticks *= 2;

        return ticks >= cap.Ticks ? cap : TimeSpan.FromTicks(ticks);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `./build.sh test`
Expected: PASS, 24 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UsagePill/Polling tests/UsagePill.Tests/BackoffPolicyTests.cs
git commit -m "feat: add the poll backoff policy"
```

---

## Task 7: Usage poller

**Files:**
- Create: `src/UsagePill/Polling/UsagePoller.cs`
- Test: `tests/UsagePill.Tests/UsagePollerTests.cs`

**Interfaces:**
- Consumes: `IUsageProvider`, `UsageState`, `UsageStatus`, `BackoffPolicy`.
- Produces: `class UsagePoller(IUsageProvider provider, BackoffPolicy backoff, TimeProvider clock) : IDisposable` with:
  - `UsageState State { get; }`
  - `event EventHandler<UsageState>? StateChanged`
  - `void Start()`, `Task RefreshNowAsync()`, `void Dispose()`

Behaviour:
- Starts in `UsageStatus.Loading`.
- Success sets `Ok` with the new snapshot.
- Failure with a previous snapshot sets `Stale`, keeping the old snapshot and filling `Message` and `RetryAt`.
- Failure with no previous snapshot sets `NoCredentials`, `AuthExpired`, or `Stale` as the exception dictates.
- A poll never throws out of the loop.
- The timer is driven by `TimeProvider` so tests can control it.

- [ ] **Step 1: Write the failing tests**

`tests/UsagePill.Tests/UsagePollerTests.cs`:

```csharp
using System.Net.Http;
using Microsoft.Extensions.Time.Testing;
using UsagePill.Core;
using UsagePill.Polling;

namespace UsagePill.Tests;

public class UsagePollerTests
{
    private sealed class StubProvider : IUsageProvider
    {
        private readonly Queue<Func<UsageSnapshot>> _responses = new();

        public string ProviderId => "stub";
        public string DisplayName => "Stub";
        public int Calls { get; private set; }

        public void EnqueueSuccess(double percent) => _responses.Enqueue(() => new UsageSnapshot(
            new[] { new UsageLimit(LimitKind.Session, percent, Severity.Normal, null, null) },
            null,
            DateTimeOffset.UnixEpoch));

        public void EnqueueFailure(Exception error) => _responses.Enqueue(() => throw error);

        public Task<UsageSnapshot> FetchAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_responses.Dequeue()());
        }
    }

    private static (UsagePoller poller, StubProvider provider, FakeTimeProvider clock) Build()
    {
        var provider = new StubProvider();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        var poller = new UsagePoller(provider, new BackoffPolicy(TimeSpan.FromMinutes(5)), clock);
        return (poller, provider, clock);
    }

    [Fact]
    public void StartsInLoading()
    {
        var (poller, _, _) = Build();

        Assert.Equal(UsageStatus.Loading, poller.State.Status);
    }

    [Fact]
    public async Task SuccessPublishesOk()
    {
        var (poller, provider, _) = Build();
        provider.EnqueueSuccess(42);

        await poller.RefreshNowAsync();

        Assert.Equal(UsageStatus.Ok, poller.State.Status);
        Assert.Equal(42, poller.State.Snapshot!.Find(LimitKind.Session)!.Percent);
    }

    [Fact]
    public async Task FailureAfterSuccessKeepsTheOldSnapshotAndMarksStale()
    {
        var (poller, provider, _) = Build();
        provider.EnqueueSuccess(42);
        provider.EnqueueFailure(new RateLimitedException(TimeSpan.FromMinutes(3)));

        await poller.RefreshNowAsync();
        await poller.RefreshNowAsync();

        Assert.Equal(UsageStatus.Stale, poller.State.Status);
        Assert.Equal(42, poller.State.Snapshot!.Find(LimitKind.Session)!.Percent);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 12, 3, 0, TimeSpan.Zero), poller.State.RetryAt);
    }

    [Fact]
    public async Task MissingCredentialsPublishNoCredentials()
    {
        var (poller, provider, _) = Build();
        provider.EnqueueFailure(new NoCredentialsException("nope"));

        await poller.RefreshNowAsync();

        Assert.Equal(UsageStatus.NoCredentials, poller.State.Status);
    }

    [Fact]
    public async Task ExpiredTokenPublishesAuthExpired()
    {
        var (poller, provider, _) = Build();
        provider.EnqueueFailure(new AuthExpiredException("nope"));

        await poller.RefreshNowAsync();

        Assert.Equal(UsageStatus.AuthExpired, poller.State.Status);
    }

    [Fact]
    public async Task EveryPublishRaisesStateChanged()
    {
        var (poller, provider, _) = Build();
        provider.EnqueueSuccess(1);
        provider.EnqueueFailure(new HttpRequestException("boom"));
        var seen = new List<UsageStatus>();
        poller.StateChanged += (_, state) => seen.Add(state.Status);

        await poller.RefreshNowAsync();
        await poller.RefreshNowAsync();

        Assert.Equal(new[] { UsageStatus.Ok, UsageStatus.Stale }, seen);
    }

    [Fact]
    public async Task TheTimerPollsAgainAfterTheBackoffDelay()
    {
        var (poller, provider, clock) = Build();
        provider.EnqueueSuccess(1);
        provider.EnqueueSuccess(2);
        poller.Start();
        await poller.WaitForIdleAsync();

        clock.Advance(TimeSpan.FromMinutes(5));
        await poller.WaitForIdleAsync();

        Assert.Equal(2, provider.Calls);
        Assert.Equal(2, poller.State.Snapshot!.Find(LimitKind.Session)!.Percent);
    }
}
```

Add the fake clock package to `tests/UsagePill.Tests/UsagePill.Tests.csproj`:

```xml
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="8.10.0" />
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `./build.sh test`
Expected: build error, `UsagePoller` does not exist.

- [ ] **Step 3: Write the implementation**

`src/UsagePill/Polling/UsagePoller.cs`:

```csharp
using UsagePill.Core;

namespace UsagePill.Polling;

/// <summary>
/// Polls one provider on a timer and publishes the resulting state. A failed poll
/// never stops the loop and never loses the last good snapshot.
/// </summary>
public sealed class UsagePoller : IDisposable
{
    private readonly IUsageProvider _provider;
    private readonly BackoffPolicy _backoff;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ITimer? _timer;
    private Task _inFlight = Task.CompletedTask;

    public UsagePoller(IUsageProvider provider, BackoffPolicy backoff, TimeProvider clock)
    {
        _provider = provider;
        _backoff = backoff;
        _clock = clock;
    }

    public UsageState State { get; private set; } = UsageState.Loading;

    public event EventHandler<UsageState>? StateChanged;

    public void Start()
    {
        _timer ??= _clock.CreateTimer(
            _ => _inFlight = RefreshNowAsync(),
            null,
            TimeSpan.Zero,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>Waits for the poll that the timer started, so tests are deterministic.</summary>
    public Task WaitForIdleAsync() => _inFlight;

    public async Task RefreshNowAsync()
    {
        await _gate.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            Exception? failure = null;
            try
            {
                var snapshot = await _provider.FetchAsync(_cts.Token).ConfigureAwait(false);
                Publish(new UsageState(UsageStatus.Ok, snapshot, null, null));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                failure = e;
            }

            var delay = _backoff.NextDelay(failure);
            if (failure is not null) Publish(BuildFailureState(failure, _clock.GetUtcNow() + delay));

            _timer?.Change(delay, Timeout.InfiniteTimeSpan);
        }
        finally
        {
            _gate.Release();
        }
    }

    private UsageState BuildFailureState(Exception failure, DateTimeOffset retryAt) => failure switch
    {
        NoCredentialsException => new UsageState(UsageStatus.NoCredentials, State.Snapshot, failure.Message, retryAt),
        AuthExpiredException => new UsageState(UsageStatus.AuthExpired, State.Snapshot, failure.Message, retryAt),
        _ => new UsageState(UsageStatus.Stale, State.Snapshot, failure.Message, retryAt),
    };

    private void Publish(UsageState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _timer?.Dispose();
        _cts.Dispose();
        _gate.Dispose();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `./build.sh test`
Expected: PASS, 31 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UsagePill/Polling tests/UsagePill.Tests
git commit -m "feat: poll usage on a timer with backoff"
```

---

## Task 8: Settings

**Files:**
- Create: `src/UsagePill/Settings/AppSettings.cs`
- Create: `src/UsagePill/Settings/SettingsStore.cs`
- Test: `tests/UsagePill.Tests/SettingsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum PillOrientation { Horizontal, Vertical }`
  - `record RingSwitches { bool Session; bool WeeklyAll; bool WeeklyPerModel; }` (init-only properties with defaults `true`)
  - `record WindowPosition(double Left, double Top)`
  - `record AppSettings` with `PollIntervalMinutes` (5), `Rings`, `RingSizePx` (32), `Orientation` (Horizontal), `ShowResetTimeText` (false), `WarnThresholdPercent` (75), `Opacity` (0.92), `Window` (40,40), `StartWithWindows` (false), plus `AppSettings Normalized()`
  - `class SettingsStore(string path)` with `static string DefaultPath()`, `AppSettings Load()`, `void Save(AppSettings settings)`

Normalization, from spec section 7:
- `Rings.Session` forced to `true`.
- `RingSizePx` clamped to 24-48.
- `PollIntervalMinutes` clamped to 1-60.
- `WarnThresholdPercent` clamped to 1-100.
- `Opacity` clamped to 0.3-1.0.
- Unknown `Orientation` values fall back to `Horizontal` (the JSON converter handles this).

- [ ] **Step 1: Write the failing tests**

`tests/UsagePill.Tests/SettingsTests.cs`:

```csharp
using UsagePill.Settings;

namespace UsagePill.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("usagepill").FullName;
    private string Path_ => System.IO.Path.Combine(_dir, "settings.json");

    [Fact]
    public void DefaultsMatchTheSpec()
    {
        var settings = new AppSettings();

        Assert.Equal(5, settings.PollIntervalMinutes);
        Assert.Equal(32, settings.RingSizePx);
        Assert.Equal(PillOrientation.Horizontal, settings.Orientation);
        Assert.Equal(75, settings.WarnThresholdPercent);
        Assert.True(settings.Rings.Session);
        Assert.True(settings.Rings.WeeklyAll);
        Assert.True(settings.Rings.WeeklyPerModel);
        Assert.False(settings.ShowResetTimeText);
    }

    [Fact]
    public void NormalizeClampsTheRingSize()
    {
        Assert.Equal(24, new AppSettings { RingSizePx = 8 }.Normalized().RingSizePx);
        Assert.Equal(48, new AppSettings { RingSizePx = 900 }.Normalized().RingSizePx);
    }

    [Fact]
    public void NormalizeForcesTheSessionRingOn()
    {
        var settings = new AppSettings { Rings = new RingSwitches { Session = false } }.Normalized();

        Assert.True(settings.Rings.Session);
    }

    [Fact]
    public void RoundTripsThroughDisk()
    {
        var store = new SettingsStore(Path_);
        store.Save(new AppSettings { RingSizePx = 40, Orientation = PillOrientation.Vertical, ShowResetTimeText = true });

        var loaded = store.Load();

        Assert.Equal(40, loaded.RingSizePx);
        Assert.Equal(PillOrientation.Vertical, loaded.Orientation);
        Assert.True(loaded.ShowResetTimeText);
    }

    [Fact]
    public void LoadReturnsDefaultsWhenTheFileIsMissing()
    {
        Assert.Equal(32, new SettingsStore(Path_).Load().RingSizePx);
    }

    [Fact]
    public void LoadReturnsDefaultsWhenTheFileIsCorrupt()
    {
        File.WriteAllText(Path_, "{ not json");

        Assert.Equal(32, new SettingsStore(Path_).Load().RingSizePx);
    }

    [Fact]
    public void LoadNormalizesWhatItReads()
    {
        File.WriteAllText(Path_, """
        { "ringSizePx": 200, "orientation": "sideways", "rings": { "session": false } }
        """);

        var loaded = new SettingsStore(Path_).Load();

        Assert.Equal(48, loaded.RingSizePx);
        Assert.Equal(PillOrientation.Horizontal, loaded.Orientation);
        Assert.True(loaded.Rings.Session);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `./build.sh test`
Expected: build error, `UsagePill.Settings` does not exist.

- [ ] **Step 3: Write the implementation**

`src/UsagePill/Settings/AppSettings.cs`:

```csharp
using System.Text.Json.Serialization;

namespace UsagePill.Settings;

public enum PillOrientation { Horizontal, Vertical }

public sealed record RingSwitches
{
    public bool Session { get; init; } = true;
    public bool WeeklyAll { get; init; } = true;
    public bool WeeklyPerModel { get; init; } = true;
}

public sealed record WindowPosition
{
    public double Left { get; init; } = 40;
    public double Top { get; init; } = 40;
}

public sealed record AppSettings
{
    public int PollIntervalMinutes { get; init; } = 5;
    public RingSwitches Rings { get; init; } = new();
    public int RingSizePx { get; init; } = 32;

    [JsonConverter(typeof(JsonStringEnumConverter<PillOrientation>))]
    public PillOrientation Orientation { get; init; } = PillOrientation.Horizontal;

    public bool ShowResetTimeText { get; init; }
    public int WarnThresholdPercent { get; init; } = 75;
    public double Opacity { get; init; } = 0.92;
    public WindowPosition Window { get; init; } = new();
    public bool StartWithWindows { get; init; }

    public AppSettings Normalized() => this with
    {
        PollIntervalMinutes = Math.Clamp(PollIntervalMinutes, 1, 60),
        RingSizePx = Math.Clamp(RingSizePx, 24, 48),
        WarnThresholdPercent = Math.Clamp(WarnThresholdPercent, 1, 100),
        Opacity = Math.Clamp(Opacity, 0.3, 1.0),
        Rings = Rings with { Session = true },
    };
}
```

`src/UsagePill/Settings/SettingsStore.cs`:

```csharp
using System.IO;
using System.Text.Json;

namespace UsagePill.Settings;

/// <summary>Loads and saves settings as JSON under %APPDATA%\usage-pill.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "usage-pill",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            var json = File.ReadAllText(_path);
            return (JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings()).Normalized();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // A missing or damaged file is not an error: fall back to the defaults.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings.Normalized(), Options));
        File.Move(temp, _path, overwrite: true);
    }
}
```

`JsonStringEnumConverter` throws on an unknown value, which would defeat the fallback. Handle it by catching `JsonException` in `Load` (already done above) - the test `LoadNormalizesWhatItReads` requires the other values to survive, so instead register a tolerant converter:

`src/UsagePill/Settings/TolerantEnumConverter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsagePill.Settings;

/// <summary>Reads an enum by name and falls back to the default on an unknown value.</summary>
public sealed class TolerantEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            Enum.TryParse<TEnum>(reader.GetString(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        if (reader.TokenType != JsonTokenType.String) reader.Skip();
        return default;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
```

Change the attribute in `AppSettings` to `[JsonConverter(typeof(TolerantEnumConverter<PillOrientation>))]`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `./build.sh test`
Expected: PASS, 38 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UsagePill/Settings tests/UsagePill.Tests/SettingsTests.cs
git commit -m "feat: add settings with clamping and atomic save"
```

---

## Task 9: Ring geometry and colour

**Files:**
- Create: `src/UsagePill/Ui/RingGeometry.cs`
- Test: `tests/UsagePill.Tests/RingGeometryTests.cs`

**Interfaces:**
- Consumes: `Severity`, `UsageLimit`, `AppSettings`.
- Produces: `static class RingGeometry` with
  - `double Thickness(int ringSizePx)`, `double FontSize(int ringSizePx)`, `double Gap(int ringSizePx)`
  - `const double CapsulePaddingLong = 5`, `const double CapsulePaddingShort = 7`
  - `Color ColorFor(double percent, Severity apiSeverity, int warnThresholdPercent)`
  - `static readonly Color Red, Amber, Green, Grey`
  - `string FormatPercent(double percent)` returning `"90"`, `"100"`, `"0"`
  - `string FormatResetIn(TimeSpan remaining)` returning `"2h 14m"`, `"14m"`, `"now"`

- [ ] **Step 1: Write the failing tests**

`tests/UsagePill.Tests/RingGeometryTests.cs`:

```csharp
using System.Windows.Media;
using UsagePill.Core;
using UsagePill.Ui;

namespace UsagePill.Tests;

public class RingGeometryTests
{
    [Theory]
    [InlineData(24, 2.256, 8.256, 6.0)]
    [InlineData(32, 3.008, 11.008, 8.0)]
    [InlineData(48, 4.512, 16.512, 12.0)]
    public void DerivedSizesFollowTheRatios(int size, double thickness, double fontSize, double gap)
    {
        Assert.Equal(thickness, RingGeometry.Thickness(size), 3);
        Assert.Equal(fontSize, RingGeometry.FontSize(size), 3);
        Assert.Equal(gap, RingGeometry.Gap(size), 3);
    }

    [Fact]
    public void CriticalFromTheApiIsAlwaysRed()
    {
        Assert.Equal(RingGeometry.Red, RingGeometry.ColorFor(2, Severity.Critical, 75));
    }

    [Fact]
    public void AtOrAboveTheThresholdIsAmber()
    {
        Assert.Equal(RingGeometry.Amber, RingGeometry.ColorFor(75, Severity.Normal, 75));
        Assert.Equal(RingGeometry.Amber, RingGeometry.ColorFor(99, Severity.Normal, 75));
    }

    [Fact]
    public void BelowTheThresholdIsGreen()
    {
        Assert.Equal(RingGeometry.Green, RingGeometry.ColorFor(74.9, Severity.Normal, 75));
    }

    [Fact]
    public void PercentsAreWholeNumbersWithoutASign()
    {
        Assert.Equal("90", RingGeometry.FormatPercent(90.4));
        Assert.Equal("100", RingGeometry.FormatPercent(100));
        Assert.Equal("0", RingGeometry.FormatPercent(0.2));
    }

    [Theory]
    [InlineData(134, "2h 14m")]
    [InlineData(14, "14m")]
    [InlineData(0, "now")]
    public void ResetTimeIsShortAndHumanReadable(int minutes, string expected)
    {
        Assert.Equal(expected, RingGeometry.FormatResetIn(TimeSpan.FromMinutes(minutes)));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `./build.sh test`
Expected: build error, `RingGeometry` does not exist.

- [ ] **Step 3: Write the implementation**

`src/UsagePill/Ui/RingGeometry.cs`:

```csharp
using System.Globalization;
using System.Windows.Media;
using UsagePill.Core;

namespace UsagePill.Ui;

/// <summary>Pure layout and colour maths, shared by the pill window and the tray icon.</summary>
public static class RingGeometry
{
    public const double CapsulePaddingLong = 5;
    public const double CapsulePaddingShort = 7;

    private const double ThicknessRatio = 0.094;
    private const double FontRatio = 0.344;
    private const double GapRatio = 0.25;

    public static readonly Color Red = Color.FromRgb(0xF2, 0x55, 0x5A);
    public static readonly Color Amber = Color.FromRgb(0xF5, 0xB7, 0x3D);
    public static readonly Color Green = Color.FromRgb(0x3E, 0xCF, 0x8E);
    public static readonly Color Grey = Color.FromRgb(0x6C, 0x76, 0x84);

    public static double Thickness(int ringSizePx) => ringSizePx * ThicknessRatio;

    public static double FontSize(int ringSizePx) => ringSizePx * FontRatio;

    public static double Gap(int ringSizePx) => ringSizePx * GapRatio;

    public static Color ColorFor(double percent, Severity apiSeverity, int warnThresholdPercent) => apiSeverity switch
    {
        Severity.Critical => Red,
        _ when percent >= warnThresholdPercent => Amber,
        _ => Green,
    };

    public static string FormatPercent(double percent) =>
        ((int)Math.Floor(Math.Clamp(percent, 0, 100))).ToString(CultureInfo.InvariantCulture);

    public static string FormatResetIn(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "now";

        var totalMinutes = (int)Math.Floor(remaining.TotalMinutes);
        if (totalMinutes == 0) return "now";

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}h {minutes}m")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}m");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `./build.sh test`
Expected: PASS, 48 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UsagePill/Ui tests/UsagePill.Tests/RingGeometryTests.cs
git commit -m "feat: add ring geometry and colour rules"
```

---

## Task 10: The ring gauge control

**Files:**
- Create: `src/UsagePill/Ui/RingGauge.cs`
- Test: none (drawing is verified visually in Task 14)

**Interfaces:**
- Consumes: `RingGeometry`.
- Produces: `class RingGauge : FrameworkElement` with dependency properties `Percent` (double), `RingColor` (Color), `Text` (string), `RingSize` (double), `TrackBrush` (Brush), `CoreBrush` (Brush), `TextBrush` (Brush), `FontSizePx` (double), `ShowStaleDot` (bool).

Drawing rules:
- The arc starts at 12 o'clock and sweeps clockwise for `Percent` of the circle.
- The track is the full circle under the arc.
- The core disc sits inside the ring and carries the text, centred.
- `ShowStaleDot` draws a 8 px grey dot at the top right, with a 2 px dark outline.

- [ ] **Step 1: Write the control**

`src/UsagePill/Ui/RingGauge.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace UsagePill.Ui;

/// <summary>One ring gauge: a track, an arc, a filled core, and the number.</summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = Register(nameof(Percent), 0d);
    public static readonly DependencyProperty RingColorProperty = Register(nameof(RingColor), RingGeometry.Grey);
    public static readonly DependencyProperty TextProperty = Register(nameof(Text), string.Empty);
    public static readonly DependencyProperty RingSizeProperty = Register(nameof(RingSize), 32d);
    public static readonly DependencyProperty FontSizePxProperty = Register(nameof(FontSizePx), 11d);
    public static readonly DependencyProperty TrackBrushProperty = Register(nameof(TrackBrush), (Brush)Brushes.Gray);
    public static readonly DependencyProperty CoreBrushProperty = Register(nameof(CoreBrush), (Brush)Brushes.Black);
    public static readonly DependencyProperty TextBrushProperty = Register(nameof(TextBrush), (Brush)Brushes.White);
    public static readonly DependencyProperty ShowStaleDotProperty = Register(nameof(ShowStaleDot), false);

    private static DependencyProperty Register<T>(string name, T defaultValue) => DependencyProperty.Register(
        name, typeof(T), typeof(RingGauge),
        new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public Color RingColor { get => (Color)GetValue(RingColorProperty); set => SetValue(RingColorProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double RingSize { get => (double)GetValue(RingSizeProperty); set => SetValue(RingSizeProperty, value); }
    public double FontSizePx { get => (double)GetValue(FontSizePxProperty); set => SetValue(FontSizePxProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush CoreBrush { get => (Brush)GetValue(CoreBrushProperty); set => SetValue(CoreBrushProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public bool ShowStaleDot { get => (bool)GetValue(ShowStaleDotProperty); set => SetValue(ShowStaleDotProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(RingSize, RingSize);

    protected override void OnRender(DrawingContext dc)
    {
        var size = RingSize;
        var thickness = size * 0.094;
        var centre = new Point(size / 2, size / 2);
        var radius = (size - thickness) / 2;

        var trackPen = new Pen(TrackBrush, thickness);
        dc.DrawEllipse(null, trackPen, centre, radius, radius);

        var percent = Math.Clamp(Percent, 0, 100);
        if (percent > 0)
        {
            var arcPen = new Pen(new SolidColorBrush(RingColor), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, arcPen, BuildArc(centre, radius, percent));
        }

        dc.DrawEllipse(CoreBrush, null, centre, radius - thickness / 2, radius - thickness / 2);

        if (!string.IsNullOrEmpty(Text))
        {
            var formatted = new FormattedText(
                Text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                FitFontSize(),
                TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(formatted, new Point(centre.X - formatted.Width / 2, centre.Y - formatted.Height / 2));
        }

        if (ShowStaleDot)
        {
            var dotCentre = new Point(size - 4, 4);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xEA, 0x14, 0x18, 0x1F)), null, dotCentre, 5, 5);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB2)), null, dotCentre, 3.5, 3.5);
        }
    }

    /// <summary>Three digits do not fit at the normal size, so shrink them.</summary>
    private double FitFontSize() => Text.Length >= 3 ? FontSizePx * 0.82 : FontSizePx;

    private static Geometry BuildArc(Point centre, double radius, double percent)
    {
        if (percent >= 100)
        {
            return new EllipseGeometry(centre, radius, radius);
        }

        var angle = percent / 100.0 * 2 * Math.PI;
        var start = new Point(centre.X, centre.Y - radius);
        var end = new Point(
            centre.X + radius * Math.Sin(angle),
            centre.Y - radius * Math.Cos(angle));

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, percent > 50, SweepDirection.Clockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `./build.sh build`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/UsagePill/Ui/RingGauge.cs
git commit -m "feat: add the ring gauge control"
```

---

## Task 11: Theme watcher

**Files:**
- Create: `src/UsagePill/Ui/ThemeWatcher.cs`
- Test: none (registry reads are verified in Task 14)

**Interfaces:**
- Consumes: nothing.
- Produces: `class ThemeWatcher : IDisposable` with `bool IsDark { get; }`, `event EventHandler? Changed`, `void Start()`.

Reads `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`, where `0` means dark. Missing value means dark. Polls the value every 5 seconds through a `DispatcherTimer`, which is simpler and more reliable than `RegNotifyChangeKeyValue` interop and costs nothing measurable.

- [ ] **Step 1: Write the implementation**

`src/UsagePill/Ui/ThemeWatcher.cs`:

```csharp
using System.Windows.Threading;
using Microsoft.Win32;

namespace UsagePill.Ui;

/// <summary>Tracks the Windows app theme so the pill can follow it.</summary>
public sealed class ThemeWatcher : IDisposable
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };

    public ThemeWatcher()
    {
        IsDark = ReadIsDark();
        _timer.Tick += (_, _) =>
        {
            var current = ReadIsDark();
            if (current == IsDark) return;
            IsDark = current;
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    public bool IsDark { get; private set; }

    public event EventHandler? Changed;

    public void Start() => _timer.Start();

    private static bool ReadIsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is not int light || light == 0;
    }

    public void Dispose() => _timer.Stop();
}
```

- [ ] **Step 2: Verify it builds**

Run: `./build.sh build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/UsagePill/Ui/ThemeWatcher.cs
git commit -m "feat: follow the Windows app theme"
```

---

## Task 12: Pill window

**Files:**
- Create: `src/UsagePill/Ui/PillWindow.xaml`, `src/UsagePill/Ui/PillWindow.xaml.cs`
- Modify: `src/UsagePill/App.xaml.cs`
- Test: none (verified visually in Task 14)

**Interfaces:**
- Consumes: `UsageState`, `AppSettings`, `RingGauge`, `RingGeometry`, `ThemeWatcher`.
- Produces: `class PillWindow : Window` with
  - `PillWindow(AppSettings settings, ThemeWatcher theme)`
  - `void Apply(UsageState state)`
  - `void Apply(AppSettings settings)`
  - `event EventHandler? LeftClicked`, `event EventHandler? RightClicked`
  - `WindowPosition CurrentPosition { get; }`

Behaviour:
- `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`, `Topmost=True`, `ShowInTaskbar=False`, `ResizeMode=NoResize`, `SizeToContent=WidthAndHeight`.
- A rounded `Border` is the capsule; its child is a `StackPanel` whose `Orientation` follows the setting.
- Rings are rebuilt when the settings change and updated in place when the state changes.
- Drag: `MouseLeftButtonDown` calls `DragMove`; on `MouseLeftButtonUp` without movement, raise `LeftClicked`; after a drag, snap to an edge or corner when within 16 px of the working area.
- `ToolTip` text is rebuilt on every `Apply(UsageState)`.

- [ ] **Step 1: Write the XAML**

`src/UsagePill/Ui/PillWindow.xaml`:

```xml
<Window x:Class="UsagePill.Ui.PillWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Usage Pill"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        SizeToContent="WidthAndHeight" UseLayoutRounding="True" SnapsToDevicePixels="True">
    <Border x:Name="Capsule"
            Background="#BD181C24"
            BorderBrush="#17FFFFFF" BorderThickness="1"
            MouseLeftButtonDown="OnCapsuleMouseDown"
            MouseLeftButtonUp="OnCapsuleMouseUp"
            MouseRightButtonUp="OnCapsuleRightClick">
        <Border.Effect>
            <DropShadowEffect BlurRadius="18" ShadowDepth="4" Opacity="0.34" Color="#000000" />
        </Border.Effect>
        <StackPanel x:Name="Rings" Orientation="Horizontal" />
    </Border>
</Window>
```

- [ ] **Step 2: Write the code-behind**

`src/UsagePill/Ui/PillWindow.xaml.cs`:

```csharp
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UsagePill.Core;
using UsagePill.Settings;

namespace UsagePill.Ui;

public partial class PillWindow : Window
{
    private const double SnapDistance = 16;

    private static readonly (LimitKind Kind, string Label)[] Order =
    {
        (LimitKind.Session, "Session"),
        (LimitKind.WeeklyAll, "Weekly, all models"),
        (LimitKind.WeeklyScoped, "Weekly, per model"),
    };

    private readonly ThemeWatcher _theme;
    private readonly List<(LimitKind Kind, RingGauge Gauge)> _gauges = new();
    private TextBlock? _resetText;

    private AppSettings _settings;
    private UsageState _state = UsageState.Loading;
    private Point _dragStart;
    private bool _dragged;

    public PillWindow(AppSettings settings, ThemeWatcher theme)
    {
        InitializeComponent();
        _settings = settings;
        _theme = theme;
        _theme.Changed += (_, _) => Dispatcher.Invoke(Rebuild);

        Left = settings.Window.Left;
        Top = settings.Window.Top;
        Rebuild();
    }

    public event EventHandler? LeftClicked;
    public event EventHandler? RightClicked;

    public WindowPosition CurrentPosition => new() { Left = Left, Top = Top };

    public void Apply(AppSettings settings)
    {
        _settings = settings;
        Rebuild();
    }

    public void Apply(UsageState state)
    {
        _state = state;
        Render();
    }

    private void Rebuild()
    {
        var size = _settings.RingSizePx;
        var gap = RingGeometry.Gap(size);

        Capsule.CornerRadius = new CornerRadius((size + 2 * RingGeometry.CapsulePaddingShort) / 2);
        Capsule.Padding = _settings.Orientation == PillOrientation.Horizontal
            ? new Thickness(RingGeometry.CapsulePaddingShort, RingGeometry.CapsulePaddingLong, RingGeometry.CapsulePaddingShort, RingGeometry.CapsulePaddingLong)
            : new Thickness(RingGeometry.CapsulePaddingLong, RingGeometry.CapsulePaddingShort, RingGeometry.CapsulePaddingLong, RingGeometry.CapsulePaddingShort);
        Capsule.Background = new SolidColorBrush(_theme.IsDark
            ? Color.FromArgb(0xBD, 0x18, 0x1C, 0x24)
            : Color.FromArgb(0xC7, 0xFA, 0xFA, 0xFC));
        Capsule.BorderBrush = new SolidColorBrush(_theme.IsDark
            ? Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x14, 0x00, 0x00, 0x00));

        Rings.Orientation = _settings.Orientation == PillOrientation.Horizontal
            ? Orientation.Horizontal
            : Orientation.Vertical;
        Rings.Children.Clear();
        _gauges.Clear();
        _resetText = null;

        foreach (var (kind, _) in Order)
        {
            if (!IsEnabled(kind)) continue;

            var gauge = new RingGauge
            {
                RingSize = size,
                FontSizePx = RingGeometry.FontSize(size),
                TrackBrush = new SolidColorBrush(_theme.IsDark
                    ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0x21, 0x00, 0x00, 0x00)),
                CoreBrush = new SolidColorBrush(_theme.IsDark
                    ? Color.FromArgb(0xDB, 0x18, 0x1C, 0x24)
                    : Color.FromArgb(0xEB, 0xFC, 0xFC, 0xFD)),
                TextBrush = new SolidColorBrush(_theme.IsDark
                    ? Color.FromRgb(0xF4, 0xF6, 0xFA)
                    : Color.FromRgb(0x16, 0x19, 0x1F)),
                Margin = _gauges.Count == 0
                    ? new Thickness(0)
                    : Rings.Orientation == Orientation.Horizontal
                        ? new Thickness(gap, 0, 0, 0)
                        : new Thickness(0, gap, 0, 0),
            };

            Rings.Children.Add(gauge);
            _gauges.Add((kind, gauge));
        }

        if (_settings.ShowResetTimeText)
        {
            _resetText = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(_theme.IsDark
                    ? Color.FromRgb(0xEA, 0xEE, 0xF5)
                    : Color.FromRgb(0x16, 0x19, 0x1F)),
                Margin = Rings.Orientation == Orientation.Horizontal
                    ? new Thickness(gap, 0, gap / 2, 0)
                    : new Thickness(0, gap, 0, 0),
            };
            Rings.Children.Add(_resetText);
        }

        Opacity = _settings.Opacity;
        Render();
    }

    private bool IsEnabled(LimitKind kind) => kind switch
    {
        LimitKind.Session => true,
        LimitKind.WeeklyAll => _settings.Rings.WeeklyAll,
        LimitKind.WeeklyScoped => _settings.Rings.WeeklyPerModel,
        _ => false,
    };

    private void Render()
    {
        var stale = _state.Status == UsageStatus.Stale;
        var snapshot = _state.Snapshot;

        for (var i = 0; i < _gauges.Count; i++)
        {
            var (kind, gauge) = _gauges[i];
            var limit = snapshot?.Find(kind);

            gauge.ShowStaleDot = stale && i == 0;

            if (_state.Status == UsageStatus.AuthExpired && i == 0)
            {
                gauge.Percent = 100;
                gauge.RingColor = RingGeometry.Amber;
                gauge.Text = "!";
                continue;
            }

            if (limit is null)
            {
                gauge.Percent = 0;
                gauge.RingColor = RingGeometry.Grey;
                gauge.Text = _state.Status == UsageStatus.Loading ? "--" : "-";
                continue;
            }

            gauge.Percent = limit.Percent;
            gauge.RingColor = RingGeometry.ColorFor(limit.Percent, limit.ApiSeverity, _settings.WarnThresholdPercent);
            gauge.Text = RingGeometry.FormatPercent(limit.Percent);
        }

        if (_resetText is not null)
        {
            var resetsAt = snapshot?.Find(LimitKind.Session)?.ResetsAt;
            _resetText.Text = resetsAt is null ? "-" : RingGeometry.FormatResetIn(resetsAt.Value - DateTimeOffset.UtcNow);
        }

        Capsule.Opacity = stale ? 0.6 : 1.0;
        ToolTip = BuildTooltip();
    }

    private string BuildTooltip()
    {
        if (_state.Status == UsageStatus.NoCredentials) return "Claude Code not logged in";
        if (_state.Status == UsageStatus.AuthExpired) return "Login expired - start Claude Code to refresh";
        if (_state.Snapshot is null) return "Loading";

        var text = new StringBuilder();
        foreach (var (kind, label) in Order)
        {
            var limit = _state.Snapshot.Find(kind);
            if (limit is null) continue;

            var name = kind == LimitKind.WeeklyScoped && limit.ScopeLabel is { } scope ? $"Weekly, {scope}" : label;
            text.Append(name).Append(' ').Append(RingGeometry.FormatPercent(limit.Percent)).Append('%');

            if (limit.ResetsAt is { } resets)
            {
                text.Append(" - resets in ").Append(RingGeometry.FormatResetIn(resets - DateTimeOffset.UtcNow));
            }
            text.AppendLine();
        }

        if (_state.Status == UsageStatus.Stale && _state.RetryAt is { } retry)
        {
            text.Append("Stale - retrying at ").Append(retry.ToLocalTime().ToString("HH:mm"));
        }

        return text.ToString().TrimEnd();
    }

    private void OnCapsuleMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragged = false;
        var before = new Point(Left, Top);
        DragMove();
        _dragged = Math.Abs(Left - before.X) > 2 || Math.Abs(Top - before.Y) > 2;
        if (_dragged) SnapToEdges();
    }

    private void OnCapsuleMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragged) LeftClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnCapsuleRightClick(object sender, MouseButtonEventArgs e)
        => RightClicked?.Invoke(this, EventArgs.Empty);

    private void SnapToEdges()
    {
        var area = SystemParameters.WorkArea;

        if (Math.Abs(Left - area.Left) <= SnapDistance) Left = area.Left;
        if (Math.Abs(area.Right - (Left + ActualWidth)) <= SnapDistance) Left = area.Right - ActualWidth;
        if (Math.Abs(Top - area.Top) <= SnapDistance) Top = area.Top;
        if (Math.Abs(area.Bottom - (Top + ActualHeight)) <= SnapDistance) Top = area.Bottom - ActualHeight;
    }
}
```

- [ ] **Step 3: Show the window from the app**

Replace `src/UsagePill/App.xaml.cs` with a temporary host so the window can be seen; Task 13 replaces this with the full composition root:

```csharp
using System.Windows;
using UsagePill.Settings;
using UsagePill.Ui;

namespace UsagePill;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = new SettingsStore(SettingsStore.DefaultPath()).Load();
        var theme = new ThemeWatcher();
        theme.Start();

        var window = new PillWindow(settings, theme);
        window.Show();
    }
}
```

- [ ] **Step 4: Run it**

Run: `./build.sh run`
Expected: a capsule with three grey rings showing `--` appears at 40,40 and stays on top. Close it with Alt+F4 for now.

- [ ] **Step 5: Commit**

```bash
git add src/UsagePill/Ui/PillWindow.xaml src/UsagePill/Ui/PillWindow.xaml.cs src/UsagePill/App.xaml.cs
git commit -m "feat: add the pill window"
```

---

## Task 13: Detail popup, settings window, tray icon, composition root

**Files:**
- Create: `src/UsagePill/Ui/DetailPopup.xaml`, `src/UsagePill/Ui/DetailPopup.xaml.cs`
- Create: `src/UsagePill/Ui/SettingsWindow.xaml`, `src/UsagePill/Ui/SettingsWindow.xaml.cs`
- Create: `src/UsagePill/Ui/TrayIcon.cs`
- Create: `src/UsagePill/Ui/StartupShortcut.cs`
- Modify: `src/UsagePill/App.xaml.cs`
- Test: none (verified in Task 14)

**Interfaces:**
- Consumes: everything from Tasks 2-12.
- Produces:
  - `class DetailPopup : Window` with `void Apply(UsageState state, string providerName)` and `void ShowNear(PillWindow pill)`
  - `class SettingsWindow : Window` with `SettingsWindow(AppSettings current)`, `event EventHandler<AppSettings>? SettingsChanged`
  - `class TrayIcon : IDisposable` with events `RefreshRequested`, `TogglePillRequested`, `SettingsRequested`, `QuitRequested`, and `void Apply(UsageState state, int warnThresholdPercent)`
  - `static class StartupShortcut` with `void Set(bool enabled)` and `bool IsEnabled()`

- [ ] **Step 1: Write the detail popup**

`src/UsagePill/Ui/DetailPopup.xaml`:

```xml
<Window x:Class="UsagePill.Ui.DetailPopup"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Usage detail"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        SizeToContent="WidthAndHeight" Deactivated="OnDeactivated">
    <Border Background="#F0202430" CornerRadius="10" BorderBrush="#17FFFFFF" BorderThickness="1" Padding="13,12">
        <StackPanel x:Name="Body" Width="246" />
    </Border>
</Window>
```

`src/UsagePill/Ui/DetailPopup.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsagePill.Core;

namespace UsagePill.Ui;

public partial class DetailPopup : Window
{
    private static readonly (LimitKind Kind, string Label)[] Order =
    {
        (LimitKind.Session, "Session"),
        (LimitKind.WeeklyAll, "Weekly, all models"),
        (LimitKind.WeeklyScoped, "Weekly, per model"),
    };

    private readonly int _warnThresholdPercent;

    public DetailPopup(int warnThresholdPercent)
    {
        InitializeComponent();
        _warnThresholdPercent = warnThresholdPercent;
    }

    public void Apply(UsageState state, string providerName)
    {
        Body.Children.Clear();
        Body.Children.Add(Header(providerName));

        if (state.Snapshot is null)
        {
            Body.Children.Add(Line(state.Status == UsageStatus.NoCredentials
                ? "Claude Code not logged in"
                : "Loading"));
            return;
        }

        foreach (var (kind, label) in Order)
        {
            var limit = state.Snapshot.Find(kind);
            if (limit is null) continue;

            var name = kind == LimitKind.WeeklyScoped && limit.ScopeLabel is { } scope ? $"Weekly, {scope}" : label;
            Body.Children.Add(Row(name, RingGeometry.FormatPercent(limit.Percent) + "%"));
            Body.Children.Add(Meter(limit));
            if (limit.ResetsAt is { } resets)
            {
                Body.Children.Add(Muted("Resets in " + RingGeometry.FormatResetIn(resets - DateTimeOffset.UtcNow)));
            }
        }

        if (state.Snapshot.Spend is { } spend)
        {
            Body.Children.Add(Row("Extra credits", $"{spend.Used:0.00} / {spend.Limit:0} {spend.Currency}"));
        }

        var age = DateTimeOffset.UtcNow - state.Snapshot.CapturedAt;
        Body.Children.Add(Muted($"Updated {RingGeometry.FormatResetIn(age)} ago"));

        if (state.Status == UsageStatus.Stale && state.Message is { } message)
        {
            Body.Children.Add(Muted(message));
        }
    }

    public void ShowNear(PillWindow pill)
    {
        Left = pill.Left;
        Top = pill.Top + pill.ActualHeight + 6;
        Show();
        Activate();
    }

    private void OnDeactivated(object? sender, EventArgs e) => Hide();

    private static TextBlock Header(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontSize = 11, FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xEE, 0xF1, 0xF6)),
        Margin = new Thickness(0, 0, 0, 8),
    };

    private static UIElement Row(string key, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = Line(key);
        var right = Line(value);
        right.FontWeight = FontWeights.SemiBold;
        Grid.SetColumn(right, 1);

        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private UIElement Meter(UsageLimit limit)
    {
        var track = new Border
        {
            Height = 4, CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 2, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var fill = new Border
        {
            Height = 4, CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(RingGeometry.ColorFor(limit.Percent, limit.ApiSeverity, _warnThresholdPercent)),
            Width = 246 * Math.Clamp(limit.Percent, 0, 100) / 100,
        };

        track.Child = fill;
        return track;
    }

    private static TextBlock Line(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontSize = 12,
        Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xF1, 0xF6)),
    };

    private static TextBlock Muted(string text)
    {
        var line = Line(text);
        line.FontSize = 11;
        line.Foreground = new SolidColorBrush(Color.FromArgb(0x8C, 0xEE, 0xF1, 0xF6));
        line.Margin = new Thickness(0, 0, 0, 6);
        return line;
    }
}
```

- [ ] **Step 2: Write the startup shortcut helper**

`src/UsagePill/Ui/StartupShortcut.cs`:

```csharp
using System.IO;

namespace UsagePill.Ui;

/// <summary>
/// Adds or removes a .cmd launcher in the user Startup folder. A script avoids the
/// COM interop a .lnk needs, and Windows runs it the same way.
/// </summary>
public static class StartupShortcut
{
    private static string Path_ => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "usage-pill.cmd");

    public static bool IsEnabled() => File.Exists(Path_);

    public static void Set(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(Path_)) File.Delete(Path_);
            return;
        }

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve the executable path.");
        File.WriteAllText(Path_, $"@echo off\r\nstart \"\" \"{exe}\"\r\n");
    }
}
```

- [ ] **Step 3: Write the settings window**

`src/UsagePill/Ui/SettingsWindow.xaml`:

```xml
<Window x:Class="UsagePill.Ui.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Usage Pill settings" Width="340" SizeToContent="Height"
        ResizeMode="NoResize" WindowStartupLocation="CenterScreen"
        Background="#FF202430" Foreground="#FFEEF1F6"
        FontFamily="Segoe UI Variable Text, Segoe UI" FontSize="12">
    <StackPanel Margin="16">
        <TextBlock Text="RINGS" Opacity="0.5" FontWeight="SemiBold" Margin="0,0,0,6" />
        <CheckBox x:Name="RingWeeklyAll" Content="Weekly, all models" Margin="0,3" Foreground="#FFEEF1F6" />
        <CheckBox x:Name="RingWeeklyPerModel" Content="Weekly, per model" Margin="0,3" Foreground="#FFEEF1F6" />

        <TextBlock Text="APPEARANCE" Opacity="0.5" FontWeight="SemiBold" Margin="0,14,0,6" />
        <DockPanel Margin="0,3">
            <TextBlock DockPanel.Dock="Right" x:Name="RingSizeLabel" Text="32 px" MinWidth="44" TextAlignment="Right" />
            <TextBlock Text="Ring size" />
        </DockPanel>
        <Slider x:Name="RingSize" Minimum="24" Maximum="48" TickFrequency="1" IsSnapToTickEnabled="True" />
        <CheckBox x:Name="Vertical" Content="Vertical layout" Margin="0,8,0,3" Foreground="#FFEEF1F6" />
        <CheckBox x:Name="ResetText" Content="Show the reset time as text" Margin="0,3" Foreground="#FFEEF1F6" />
        <DockPanel Margin="0,8,0,3">
            <TextBlock DockPanel.Dock="Right" x:Name="OpacityLabel" Text="92%" MinWidth="44" TextAlignment="Right" />
            <TextBlock Text="Opacity" />
        </DockPanel>
        <Slider x:Name="OpacitySlider" Minimum="30" Maximum="100" TickFrequency="1" IsSnapToTickEnabled="True" />

        <TextBlock Text="DATA" Opacity="0.5" FontWeight="SemiBold" Margin="0,14,0,6" />
        <DockPanel Margin="0,3">
            <TextBlock DockPanel.Dock="Right" x:Name="IntervalLabel" Text="5 min" MinWidth="44" TextAlignment="Right" />
            <TextBlock Text="Refresh every" />
        </DockPanel>
        <Slider x:Name="Interval" Minimum="1" Maximum="60" TickFrequency="1" IsSnapToTickEnabled="True" />
        <DockPanel Margin="0,8,0,3">
            <TextBlock DockPanel.Dock="Right" x:Name="ThresholdLabel" Text="75%" MinWidth="44" TextAlignment="Right" />
            <TextBlock Text="Amber above" />
        </DockPanel>
        <Slider x:Name="Threshold" Minimum="1" Maximum="100" TickFrequency="1" IsSnapToTickEnabled="True" />

        <CheckBox x:Name="StartWithWindows" Content="Start with Windows" Margin="0,14,0,0" Foreground="#FFEEF1F6" />

        <Button Content="Close" Click="OnClose" Margin="0,16,0,0" Padding="14,5" HorizontalAlignment="Right" />
    </StackPanel>
</Window>
```

`src/UsagePill/Ui/SettingsWindow.xaml.cs`:

```csharp
using System.Globalization;
using System.Windows;
using UsagePill.Settings;

namespace UsagePill.Ui;

public partial class SettingsWindow : Window
{
    private AppSettings _current;
    private bool _loading = true;

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        _current = current;

        RingWeeklyAll.IsChecked = current.Rings.WeeklyAll;
        RingWeeklyPerModel.IsChecked = current.Rings.WeeklyPerModel;
        RingSize.Value = current.RingSizePx;
        Vertical.IsChecked = current.Orientation == PillOrientation.Vertical;
        ResetText.IsChecked = current.ShowResetTimeText;
        OpacitySlider.Value = current.Opacity * 100;
        Interval.Value = current.PollIntervalMinutes;
        Threshold.Value = current.WarnThresholdPercent;
        StartWithWindows.IsChecked = current.StartWithWindows;

        _loading = false;
        Hook();
        UpdateLabels();
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    private void Hook()
    {
        RingWeeklyAll.Checked += (_, _) => Publish();
        RingWeeklyAll.Unchecked += (_, _) => Publish();
        RingWeeklyPerModel.Checked += (_, _) => Publish();
        RingWeeklyPerModel.Unchecked += (_, _) => Publish();
        Vertical.Checked += (_, _) => Publish();
        Vertical.Unchecked += (_, _) => Publish();
        ResetText.Checked += (_, _) => Publish();
        ResetText.Unchecked += (_, _) => Publish();
        StartWithWindows.Checked += (_, _) => Publish();
        StartWithWindows.Unchecked += (_, _) => Publish();
        RingSize.ValueChanged += (_, _) => Publish();
        OpacitySlider.ValueChanged += (_, _) => Publish();
        Interval.ValueChanged += (_, _) => Publish();
        Threshold.ValueChanged += (_, _) => Publish();
    }

    private void Publish()
    {
        if (_loading) return;

        _current = _current with
        {
            Rings = new RingSwitches
            {
                Session = true,
                WeeklyAll = RingWeeklyAll.IsChecked == true,
                WeeklyPerModel = RingWeeklyPerModel.IsChecked == true,
            },
            RingSizePx = (int)RingSize.Value,
            Orientation = Vertical.IsChecked == true ? PillOrientation.Vertical : PillOrientation.Horizontal,
            ShowResetTimeText = ResetText.IsChecked == true,
            Opacity = OpacitySlider.Value / 100,
            PollIntervalMinutes = (int)Interval.Value,
            WarnThresholdPercent = (int)Threshold.Value,
            StartWithWindows = StartWithWindows.IsChecked == true,
        };

        UpdateLabels();
        SettingsChanged?.Invoke(this, _current.Normalized());
    }

    private void UpdateLabels()
    {
        RingSizeLabel.Text = ((int)RingSize.Value).ToString(CultureInfo.InvariantCulture) + " px";
        OpacityLabel.Text = ((int)OpacitySlider.Value).ToString(CultureInfo.InvariantCulture) + "%";
        IntervalLabel.Text = ((int)Interval.Value).ToString(CultureInfo.InvariantCulture) + " min";
        ThresholdLabel.Text = ((int)Threshold.Value).ToString(CultureInfo.InvariantCulture) + "%";
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 4: Write the tray icon**

`src/UsagePill/Ui/TrayIcon.cs`:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using UsagePill.Core;

namespace UsagePill.Ui;

/// <summary>Tray icon that draws the session percentage, plus the shared context menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon = new();
    private Icon? _current;

    public TrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh now", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Show or hide pill", null, (_, _) => TogglePillRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));

        _icon.ContextMenuStrip = menu;
        _icon.Text = "Usage Pill";
        _icon.Visible = true;
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? TogglePillRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? QuitRequested;

    public void ShowContextMenu() => _icon.GetType()
        .GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(_icon, null);

    public void Apply(UsageState state, int warnThresholdPercent)
    {
        var session = state.Snapshot?.Find(LimitKind.Session);
        var text = session is null ? "-" : RingGeometry.FormatPercent(session.Percent);
        var wpfColor = session is null
            ? RingGeometry.Grey
            : RingGeometry.ColorFor(session.Percent, session.ApiSeverity, warnThresholdPercent);

        _icon.Text = session is null ? "Usage Pill - no data" : $"Claude session {text}%";

        var next = Render(text, Color.FromArgb(wpfColor.R, wpfColor.G, wpfColor.B), session?.Percent ?? 0);
        _icon.Icon = next;
        _current?.Dispose();
        _current = next;
    }

    private static Icon Render(string text, Color color, double percent)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            using var track = new Pen(Color.FromArgb(70, 255, 255, 255), 3.5f);
            g.DrawEllipse(track, 2f, 2f, 28f, 28f);

            using var arc = new Pen(color, 3.5f);
            g.DrawArc(arc, 2f, 2f, 28f, 28f, -90f, (float)(Math.Clamp(percent, 0, 100) / 100 * 360));

            using var font = new Font("Segoe UI", text.Length >= 3 ? 9f : 11f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.White);
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, brush, (32 - size.Width) / 2, (32 - size.Height) / 2);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();
    }
}
```

- [ ] **Step 5: Write the composition root**

Replace `src/UsagePill/App.xaml.cs`:

```csharp
using System.Net.Http;
using System.Windows;
using UsagePill.Claude;
using UsagePill.Core;
using UsagePill.Polling;
using UsagePill.Settings;
using UsagePill.Ui;

namespace UsagePill;

public partial class App : Application
{
    private SettingsStore _settingsStore = null!;
    private AppSettings _settings = null!;
    private ThemeWatcher _theme = null!;
    private PillWindow _pill = null!;
    private DetailPopup _detail = null!;
    private TrayIcon _tray = null!;
    private UsagePoller _poller = null!;
    private HttpClient _http = null!;
    private IUsageProvider _provider = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsStore = new SettingsStore(SettingsStore.DefaultPath());
        _settings = _settingsStore.Load();

        _theme = new ThemeWatcher();
        _theme.Start();

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _provider = new ClaudeUsageProvider(new ClaudeCredentialStore(ClaudeCredentialStore.DefaultPath()), _http, TimeProvider.System);

        _poller = new UsagePoller(_provider, new BackoffPolicy(TimeSpan.FromMinutes(_settings.PollIntervalMinutes)), TimeProvider.System);
        _poller.StateChanged += (_, state) => Dispatcher.Invoke(() => Render(state));

        _pill = new PillWindow(_settings, _theme);
        _detail = new DetailPopup(_settings.WarnThresholdPercent);
        _tray = new TrayIcon();

        _pill.LeftClicked += (_, _) => ToggleDetail();
        _pill.RightClicked += (_, _) => _tray.ShowContextMenu();

        _tray.RefreshRequested += async (_, _) => await _poller.RefreshNowAsync();
        _tray.TogglePillRequested += (_, _) => TogglePill();
        _tray.SettingsRequested += (_, _) => OpenSettings();
        _tray.QuitRequested += (_, _) => Shutdown();

        _pill.Show();
        _poller.Start();
    }

    private void Render(UsageState state)
    {
        _pill.Apply(state);
        _detail.Apply(state, _provider.DisplayName);
        _tray.Apply(state, _settings.WarnThresholdPercent);
    }

    private void ToggleDetail()
    {
        if (_detail.IsVisible) _detail.Hide();
        else _detail.ShowNear(_pill);
    }

    private void TogglePill()
    {
        if (_pill.IsVisible) _pill.Hide();
        else _pill.Show();
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings);
        window.SettingsChanged += (_, updated) =>
        {
            var startupChanged = updated.StartWithWindows != _settings.StartWithWindows;
            _settings = updated;
            _pill.Apply(updated);
            Render(_poller.State);
            if (startupChanged) StartupShortcut.Set(updated.StartWithWindows);
            _settingsStore.Save(updated with { Window = _pill.CurrentPosition });
        };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settingsStore.Save(_settings with { Window = _pill.CurrentPosition });
        _poller.Dispose();
        _tray.Dispose();
        _theme.Dispose();
        _http.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 6: Build and run**

Run: `./build.sh build` then `./build.sh run`
Expected: the pill appears with live values within a few seconds, and a tray icon shows the session percentage.

- [ ] **Step 7: Commit**

```bash
git add src/UsagePill
git commit -m "feat: add detail popup, settings, tray icon, and wiring"
```

---

## Task 14: End-to-end verification on Windows

**Files:**
- Create: `tools/screenshot.ps1`
- Test: manual, with screenshots

This task produces the evidence that the app matches `docs/superpowers/specs/2026-09-14-usage-pill-ui.html`.

- [ ] **Step 1: Write the screenshot helper**

`tools/screenshot.ps1`:

```powershell
# Captures a screen region and saves it scaled up, so pixel detail is reviewable.
# Usage: powershell.exe -File screenshot.ps1 <x> <y> <width> <height> <outputPath> [scale]
Add-Type -AssemblyName System.Drawing
$x = [int]$args[0]; $y = [int]$args[1]; $w = [int]$args[2]; $h = [int]$args[3]
$out = $args[4]; $scale = if ($args.Count -gt 5) { [int]$args[5] } else { 4 }

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
$g.Dispose()

$big = New-Object System.Drawing.Bitmap ($w * $scale), ($h * $scale)
$gb = [System.Drawing.Graphics]::FromImage($big)
$gb.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$gb.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$gb.DrawImage($bmp, 0, 0, ($w * $scale), ($h * $scale))
$gb.Dispose()
$big.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "saved $out"
```

- [ ] **Step 2: Run the app and capture the pill**

```bash
cd /home/kcao/Projects/usage-pill
(./build.sh run &) ; sleep 12
PS=/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe
"$PS" -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w tools/screenshot.ps1)" 20 20 200 80 "C:\Windows\Temp\pill.png" 4
```

Read `/mnt/c/Windows/Temp/pill.png` and compare it against section 1 of the UI reference.

**Checklist, each item must hold:**
- Three rings, left to right, in the order Session, Weekly, Fable.
- The arc starts at 12 o'clock and runs clockwise.
- The session ring is red at 90%, green below the threshold.
- The number is centred with no `%` sign; `100` is visibly smaller so it fits.
- The capsule is fully rounded with a soft shadow, and no square corners show.
- No text is clipped at any ring size.

- [ ] **Step 3: Verify every interaction**

Run each and confirm:
- Drag the pill near a screen edge; on release it lands flush with the edge.
- Quit and restart; the pill reappears in the same place.
- Click the pill; the detail card opens under it and lists all three limits, the reset times, and the credits. Click elsewhere; it hides.
- Right click the pill; the tray menu appears.
- Open settings; move the size slider from 24 to 48 and confirm the pill resizes live and stays sharp.
- Switch on "Vertical layout"; the rings stack in the same order.
- Switch off "Weekly, per model"; the pill shrinks to two rings.
- Switch on "Show the reset time as text"; the text appears inside the capsule.
- Change the Windows app theme between light and dark; the pill follows within 5 seconds.
- Tray "Refresh now" updates the "Updated N ago" line in the detail card.
- Tray "Quit" ends the process and removes the tray icon.

- [ ] **Step 4: Verify the failure states**

- Rename `%USERPROFILE%\.claude\.credentials.json` to `.bak`, click "Refresh now", and confirm the rings go grey with `-` and the tooltip says "Claude Code not logged in". Rename it back.
- Confirm the app is still running and recovers on the next refresh.

- [ ] **Step 5: Capture the final evidence**

Take one screenshot of the pill and one of the pill with the detail card open. Keep both in the task report.

- [ ] **Step 6: Commit**

```bash
git add tools/screenshot.ps1
git commit -m "chore: add the screenshot helper used for UI verification"
```

---

## Task 15: Publish and README

**Files:**
- Create: `README.md`
- Test: manual

- [ ] **Step 1: Publish a runnable build**

```bash
./build.sh publish
```

Expected: `src/UsagePill/bin/Release/net8.0-windows/win-x64/publish/UsagePill.exe` exists. Run it directly from Windows and confirm the pill appears.

- [ ] **Step 2: Write the README**

`README.md`:

```markdown
# Usage Pill

A small always-on-top Windows pill that shows your Claude usage limits as ring gauges.

## What it shows

Three rings, left to right: the 5 hour session window, the weekly all-model limit,
and the weekly per-model limit. The number inside each ring is the percentage used.

## How it gets the data

It reads the access token that Claude Code stores in
`%USERPROFILE%\.claude\.credentials.json` and calls
`https://api.anthropic.com/api/oauth/usage`, the same endpoint behind the `/usage`
command in Claude Code. The app only reads that file. It never writes to it and
never refreshes the token.

If you have not used Claude Code for several hours the token expires and the pill
shows `!` until you start Claude Code again.

## Build and run

The Windows .NET 8 SDK is required. From WSL:

```bash
./build.sh build     # compile
./build.sh test      # run unit tests
./build.sh run       # run the app
./build.sh publish   # produce UsagePill.exe
```

## Settings

`%APPDATA%\usage-pill\settings.json`, or the Settings window from the tray menu:
which rings to show, ring size (24-48 px), horizontal or vertical, opacity,
refresh interval, the amber threshold, and start with Windows.

## Design documents

- Spec: `docs/superpowers/specs/2026-09-14-usage-pill-design.md`
- Visual reference: `docs/superpowers/specs/2026-09-14-usage-pill-ui.html`
- Implementation plan: `docs/superpowers/plans/2026-09-14-usage-pill.md`
```

- [ ] **Step 3: Run the full test suite one last time**

Run: `./build.sh test`
Expected: PASS, all tests.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: add the README"
```

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| 2 Data source, parsing rules | 3, 4, 5 |
| 3 Architecture, file layout | 1, 2, and the File Structure table |
| 4 Data flow, re-read credentials per poll | 5, 7, 13 |
| 5.1-5.2 Shape and geometry | 9, 10, 12 |
| 5.3 Behaviour: switches, orientation, drag, snap, hover, click, theme | 11, 12, 13 |
| 5.4 Colour | 9, 10, 12 |
| 5.5 Optional text tail | 12, 13 |
| 6 Error handling table | 6, 7, 12, 14 |
| 7 Settings and clamping | 8, 13 |
| 8 Testing | tests in 2-9, manual in 14 |
| 9 Non-goals | nothing builds them |

**Type consistency:** `UsageSnapshot.Find(LimitKind)`, `UsageState(Status, Snapshot, Message, RetryAt)`, `IUsageProvider.FetchAsync`, `BackoffPolicy.NextDelay`, `RingGeometry.ColorFor/FormatPercent/FormatResetIn`, `AppSettings.Normalized`, and `SettingsStore.Load/Save` are named identically wherever they appear.

**Placeholders:** none. Every step carries the code it needs.
