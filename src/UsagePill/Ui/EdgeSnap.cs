using System.Windows;

namespace UsagePill.Ui;

/// <summary>
/// Where the capsule sits relative to its monitor's work area: snapped to, and kept,
/// <see cref="Gap"/> inside each edge, so it floats just off the screen edge instead of
/// touching it.
/// </summary>
public static class EdgeSnap
{
    /// <summary>How far a snapped capsule floats inside the work-area edge.</summary>
    public const double Gap = 8;

    /// <summary>How far short of its snapped position a released capsule still snaps.</summary>
    public const double Distance = 16;

    /// <summary>
    /// The capsule's new top-left corner, in the same units as <paramref name="area"/>. An edge
    /// counts as near when the capsule stops within <see cref="Distance"/> of its snapped
    /// position or anywhere past it, so a pill flung into a corner lands <see cref="Gap"/> inside
    /// it instead of hanging off the screen.
    ///
    /// Windows' drag loop stops a window's top at the work area's top edge, so a drag can bring
    /// the capsule no closer to that edge than its offset inside the window,
    /// <paramref name="topReach"/>. The top snap counts its distance from there when that is
    /// further out than the gap, or a capsule pushed as high as it goes would never snap.
    /// </summary>
    public static Point Snap(Rect capsule, Rect area, double topReach)
    {
        var left = capsule.Left;
        var top = capsule.Top;

        if (left - area.Left <= Gap + Distance) left = area.Left + Gap;
        if (area.Right - (left + capsule.Width) <= Gap + Distance) left = area.Right - Gap - capsule.Width;
        if (top - area.Top <= Math.Max(Gap, topReach) + Distance) top = area.Top + Gap;
        if (area.Bottom - (top + capsule.Height) <= Gap + Distance) top = area.Bottom - Gap - capsule.Height;

        return new Point(left, top);
    }

    /// <summary>
    /// The capsule's top-left corner moved just far enough to sit at least <see cref="Gap"/>
    /// inside every edge, or unchanged when it already does. A capsule too big to fit pins to
    /// the left and top gaps.
    /// </summary>
    public static Point Constrain(Rect capsule, Rect area)
    {
        var minLeft = area.Left + Gap;
        var maxLeft = Math.Max(minLeft, area.Right - Gap - capsule.Width);
        var minTop = area.Top + Gap;
        var maxTop = Math.Max(minTop, area.Bottom - Gap - capsule.Height);

        return new Point(
            Math.Min(Math.Max(capsule.Left, minLeft), maxLeft),
            Math.Min(Math.Max(capsule.Top, minTop), maxTop));
    }
}
