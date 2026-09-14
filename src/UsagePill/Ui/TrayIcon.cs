using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using UsagePill.Core;

namespace UsagePill.Ui;

/// <summary>Tray icon that draws the session percentage, plus the shared context menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon = new();

    // NotifyIcon shows its own ContextMenuStrip automatically, with correct dismiss-on-outside-
    // click behaviour, only when the user right-clicks the icon itself. We invoke the same menu
    // from a right-click on the pill instead, so ContextMenuStrip.Show must be called directly.
    // That path skips the activation NotifyIcon normally performs before showing, so the menu
    // never receives focus and will not dismiss on an outside click. A tiny invisible owner
    // window, activated with the documented SetForegroundWindow API right before Show, restores
    // that behaviour without reaching into NotifyIcon's private ShowContextMenu via reflection.
    private readonly Form _owner = new()
    {
        ShowInTaskbar = false,
        FormBorderStyle = FormBorderStyle.None,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-32000, -32000),
        Size = new Size(1, 1),
    };

    private Icon? _current;

    public TrayIcon()
    {
        _owner.CreateControl();

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

    public void ShowContextMenu()
    {
        NativeMethods.SetForegroundWindow(_owner.Handle);
        _icon.ContextMenuStrip!.Show(System.Windows.Forms.Cursor.Position);
    }

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
        _owner.Dispose();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
