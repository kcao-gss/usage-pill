using System.Text.Json.Serialization;

namespace UsagePill.Settings;

public enum PillOrientation { Horizontal, Vertical }

/// <summary>The pill's surface colors. System, the default, follows the Windows app theme.</summary>
public enum BackgroundTheme { System, Dark, Light, Amoled }

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
    /// <summary>The ring color below the amber threshold, before any setting changes it.</summary>
    public const string DefaultRingColor = "#3ECF8E";

    public int PollIntervalMinutes { get; init; } = 5;
    public RingSwitches Rings { get; init; } = new();
    public int RingSizePx { get; init; } = 32;

    [JsonConverter(typeof(TolerantEnumConverter<PillOrientation>))]
    public PillOrientation Orientation { get; init; } = PillOrientation.Horizontal;

    public bool ShowResetTimeText { get; init; }

    [JsonConverter(typeof(TolerantEnumConverter<BackgroundTheme>))]
    public BackgroundTheme Background { get; init; } = BackgroundTheme.System;

    /// <summary>
    /// "#RRGGBB". Only the normal state takes this color: amber above the threshold and red
    /// for a limit the API reports as critical still win, so a warning always stands out.
    /// </summary>
    public string RingColor { get; init; } = DefaultRingColor;

    public int WarnThresholdPercent { get; init; } = 75;
    public double Opacity { get; init; } = 0.92;
    public WindowPosition Window { get; init; } = new();
    public bool StartWithWindows { get; init; }

    public AppSettings Normalized()
    {
        // System.Text.Json ignores the non-nullable annotations, so a settings file holding
        // "rings": null or "window": null lands a null on these properties. Substitute a fresh
        // instance rather than let the load throw.
        var rings = Rings ?? new RingSwitches();

        return this with
        {
            PollIntervalMinutes = Math.Clamp(PollIntervalMinutes, 1, 60),
            RingSizePx = Math.Clamp(RingSizePx, 24, 48),
            WarnThresholdPercent = Math.Clamp(WarnThresholdPercent, 1, 100),
            Opacity = Math.Clamp(Opacity, 0.3, 1.0),
            Rings = rings with { Session = true },
            Window = Window ?? new WindowPosition(),
            RingColor = IsHexColor(RingColor) ? RingColor.ToUpperInvariant() : DefaultRingColor,
        };
    }

    private static bool IsHexColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
}
