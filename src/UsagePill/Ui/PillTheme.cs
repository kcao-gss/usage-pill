using System.Globalization;
using System.Windows.Media;
using UsagePill.Settings;

namespace UsagePill.Ui;

/// <summary>
/// Every surface color the pill and its detail card draw, for one background choice. The ring
/// arc itself is not here: it comes from the ring color setting and the usage state.
/// </summary>
public sealed record PillTheme(
    Color CapsuleBackground,
    Color CapsuleBorder,
    Color RingTrack,
    Color RingCore,
    Color RingText,
    Color ResetText,
    Color CardBackground,
    Color CardBorder)
{
    // The detail card has only ever had a dark design, so every theme but AMOLED keeps it.
    private static readonly Color DarkCard = Color.FromArgb(0xF0, 0x20, 0x24, 0x30);
    private static readonly Color DarkCardBorder = Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF);

    private static readonly PillTheme Dark = new(
        CapsuleBackground: Color.FromArgb(0xBD, 0x18, 0x1C, 0x24),
        CapsuleBorder: Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF),
        RingTrack: Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF),
        RingCore: Color.FromArgb(0xDB, 0x18, 0x1C, 0x24),
        RingText: Color.FromRgb(0xF4, 0xF6, 0xFA),
        ResetText: Color.FromRgb(0xEA, 0xEE, 0xF5),
        CardBackground: DarkCard,
        CardBorder: DarkCardBorder);

    private static readonly PillTheme Light = new(
        CapsuleBackground: Color.FromArgb(0xC7, 0xFA, 0xFA, 0xFC),
        CapsuleBorder: Color.FromArgb(0x14, 0x00, 0x00, 0x00),
        RingTrack: Color.FromArgb(0x21, 0x00, 0x00, 0x00),
        RingCore: Color.FromArgb(0xEB, 0xFC, 0xFC, 0xFD),
        RingText: Color.FromRgb(0x16, 0x19, 0x1F),
        ResetText: Color.FromRgb(0x16, 0x19, 0x1F),
        CardBackground: DarkCard,
        CardBorder: DarkCardBorder);

    // Solid black for OLED panels. The border is a touch brighter than Dark's, because on a black
    // desktop it is the only thing that outlines the pill.
    private static readonly PillTheme Amoled = new(
        CapsuleBackground: Colors.Black,
        CapsuleBorder: Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
        RingTrack: Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF),
        RingCore: Colors.Black,
        RingText: Color.FromRgb(0xF4, 0xF6, 0xFA),
        ResetText: Color.FromRgb(0xEA, 0xEE, 0xF5),
        CardBackground: Colors.Black,
        CardBorder: Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));

    /// <summary>The ring colors offered in Settings. Any other "#RRGGBB" works from settings.json.</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> RingSwatches =
    [
        ("Green", AppSettings.DefaultRingColor),
        ("Blue", "#4F8EF7"),
        ("Cyan", "#38C6E0"),
        ("Violet", "#9D7CF4"),
        ("Pink", "#F272B6"),
        ("White", "#E8EBF0"),
    ];

    public static PillTheme For(BackgroundTheme background, bool windowsIsDark) => background switch
    {
        BackgroundTheme.Dark => Dark,
        BackgroundTheme.Light => Light,
        BackgroundTheme.Amoled => Amoled,
        _ => windowsIsDark ? Dark : Light,
    };

    /// <summary>Reads a ring color that <see cref="AppSettings.Normalized"/> has already validated as "#RRGGBB".</summary>
    public static Color ParseRingColor(string hex)
    {
        var rgb = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
