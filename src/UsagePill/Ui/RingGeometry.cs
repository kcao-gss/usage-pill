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
        if (hours >= 24)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{hours / 24}d {hours % 24}h");
        }
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}h {minutes}m")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}m");
    }
}
