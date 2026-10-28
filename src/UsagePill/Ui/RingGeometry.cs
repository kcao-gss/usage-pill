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
    public static readonly Color Grey = Color.FromRgb(0x6C, 0x76, 0x84);

    public static double Thickness(int ringSizePx) => ringSizePx * ThicknessRatio;

    public static double FontSize(int ringSizePx) => ringSizePx * FontRatio;

    public static double Gap(int ringSizePx) => ringSizePx * GapRatio;

    /// <summary>Red and amber are fixed so a warning always stands out; <paramref name="normal"/> is the chosen ring color.</summary>
    public static Color ColorFor(double percent, Severity apiSeverity, int warnThresholdPercent, Color normal) => apiSeverity switch
    {
        Severity.Critical => Red,
        _ when percent >= warnThresholdPercent => Amber,
        _ => normal,
    };

    public static string FormatPercent(double percent) =>
        ((int)Math.Floor(Math.Clamp(percent, 0, 100))).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A pace ring is a share of a spend budget. It can run past 100, and going over is what it
    /// must flag, so it is red once the number shown passes 100 rather than when the API says so.
    /// </summary>
    public static Color PaceColorFor(double percent, int warnThresholdPercent, Color normal) => percent switch
    {
        >= 101 => Red,
        _ when percent >= warnThresholdPercent => Amber,
        _ => normal,
    };

    /// <summary>Whole numbers past 100, capped at three digits so the number still fits the ring.</summary>
    public static string FormatPacePercent(double percent) =>
        ((int)Math.Floor(Math.Clamp(percent, 0, 999))).ToString(CultureInfo.InvariantCulture);

    public static string FormatResetIn(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "now";

        var totalMinutes = (int)Math.Floor(remaining.TotalMinutes);
        if (totalMinutes == 0) return "now";

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours >= 24)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{hours / 24}d {hours % 24}h");
        }
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}h {minutes}m")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}m");
    }
}
