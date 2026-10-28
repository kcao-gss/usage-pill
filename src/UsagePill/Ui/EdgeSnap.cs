using System.Windows;

namespace UsagePill.Ui;

/// <summary>Where a dragged capsule lands: flush against every work-area edge it was released near.</summary>
public static class EdgeSnap
{
    public const double Distance = 16;

    /// <summary>
    /// The capsule's new top-left corner, in the same units as <paramref name="area"/>. An edge
    /// counts as near when the capsule stops within <see cref="Distance"/> of it or anywhere
    /// past it, so a pill flung into a corner lands flush instead of hanging off the screen.
    ///
    /// Windows' drag loop stops a window's top at the work area's top edge, so a drag can bring
    /// the capsule no closer to that edge than its offset inside the window,
    /// <paramref name="topReach"/>. The top snap counts its distance from there, or a capsule
    /// pushed as high as it goes would never snap.
    /// </summary>
    public static Point Snap(Rect capsule, Rect area, double topReach)
    {
        var left = capsule.Left;
        var top = capsule.Top;

        if (left - area.Left <= Distance) left = area.Left;
        if (area.Right - (left + capsule.Width) <= Distance) left = area.Right - capsule.Width;
        if (top - area.Top <= topReach + Distance) top = area.Top;
        if (area.Bottom - (top + capsule.Height) <= Distance) top = area.Bottom - capsule.Height;

        return new Point(left, top);
    }
}
