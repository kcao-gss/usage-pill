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

    [JsonConverter(typeof(TolerantEnumConverter<PillOrientation>))]
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
