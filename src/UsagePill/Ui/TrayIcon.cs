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
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _startWithWindowsItem;

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

    // Icon.FromHandle wraps a native HICON without taking ownership of it, so Icon.Dispose frees
    // only the managed wrapper. The handle itself is destroyed explicitly, once the icon it backs
    // has been replaced, to avoid leaking one HICON per Apply (every poll, and every settings
    // change while the threshold or session percentage is visible).
    private IntPtr _currentIconHandle;

    public TrayIcon()
    {
        _menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            BackColor = DarkMenuRenderer.Background,
            ForeColor = DarkMenuRenderer.Text,
            Font = new Font("Segoe UI", 9f),
        };
        _menu.Opening += (_, _) => _menu.Region = new Region(RoundedRectPath(new Rectangle(Point.Empty, _menu.Size), 8));

        _menu.Items.Add("Refresh now", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add("Show or hide pill", null, (_, _) => TogglePillRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Settings...", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));

        _startWithWindowsItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = false };
        _startWithWindowsItem.Click += (_, _) => StartWithWindowsToggled?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(_startWithWindowsItem);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));

        _icon.ContextMenuStrip = _menu;
        _icon.Text = "Usage Pill";
        _icon.Visible = true;
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? TogglePillRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? StartWithWindowsToggled;
    public event EventHandler? QuitRequested;

    /// <summary>
    /// The composition root owns reconciling this with the actual Startup folder contents (the
    /// checkbox must never claim a state the filesystem does not back).
    /// </summary>
    public bool StartWithWindowsChecked
    {
        get => _startWithWindowsItem.Checked;
        set => _startWithWindowsItem.Checked = value;
    }

    /// <summary>
    /// Surfaces a background failure (for example a Startup-folder shortcut write that hit a
    /// COM or ACL error) as a balloon tip anchored to the tray icon, instead of swallowing it
    /// silently or letting it escape to the unhandled-exception handler.
    /// </summary>
    public void ShowError(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = ToolTipIcon.Error;
        _icon.ShowBalloonTip(5000);
    }

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

        var (next, handle) = Render(text, Color.FromArgb(wpfColor.R, wpfColor.G, wpfColor.B), session?.Percent ?? 0);
        _icon.Icon = next;
        _current?.Dispose();
        if (_currentIconHandle != IntPtr.Zero) NativeMethods.DestroyIcon(_currentIconHandle);
        _current = next;
        _currentIconHandle = handle;
    }

    private static (Icon Icon, IntPtr Handle) Render(string text, Color color, double percent)
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

        var handle = bitmap.GetHicon();
        return (Icon.FromHandle(handle), handle);
    }

    // Mirrors the mockup's `.tray` panel (section 8): a dark rounded panel with a hairline
    // light border and a subtle highlight on the hovered item, instead of the OS default light
    // menu chrome.
    private static GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        var path = new GraphicsPath();

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => DarkMenuRenderer.Background;
        public override Color ImageMarginGradientBegin => DarkMenuRenderer.Background;
        public override Color ImageMarginGradientMiddle => DarkMenuRenderer.Background;
        public override Color ImageMarginGradientEnd => DarkMenuRenderer.Background;
        public override Color MenuBorder => DarkMenuRenderer.Border;
        public override Color MenuItemBorder => DarkMenuRenderer.Highlight;
        public override Color SeparatorDark => DarkMenuRenderer.Border;
        public override Color SeparatorLight => DarkMenuRenderer.Border;
        public override Color MenuItemSelected => DarkMenuRenderer.Highlight;
        public override Color MenuItemSelectedGradientBegin => DarkMenuRenderer.Highlight;
        public override Color MenuItemSelectedGradientEnd => DarkMenuRenderer.Highlight;
        public override Color MenuItemPressedGradientBegin => DarkMenuRenderer.Highlight;
        public override Color MenuItemPressedGradientEnd => DarkMenuRenderer.Highlight;
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        // rgba(43,47,56,.96) from the mockup's `.tray` rule, approximated as opaque since a
        // ContextMenuStrip does not composite against the desktop behind it.
        public static readonly Color Background = Color.FromArgb(0x2B, 0x2F, 0x38);
        public static readonly Color Text = Color.FromArgb(0xEE, 0xF1, 0xF6);
        // rgba(255,255,255,.09) composited over Background - used for both the hairline border
        // and the hovered-item highlight, matching `.tray` and `.tray div.hi` in the mockup.
        public static readonly Color Border = Color.FromArgb(0x3E, 0x42, 0x4A);
        public static readonly Color Highlight = Color.FromArgb(0x3E, 0x42, 0x4A);

        public DarkMenuRenderer() : base(new DarkMenuColors())
        {
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Background);
            using var path = RoundedRectPath(new Rectangle(Point.Empty, e.ToolStrip.Size), 8);
            e.Graphics.FillPath(brush, path);
        }

        // ToolStripProfessionalRenderer's default selection painting defers to the OS's visual
        // style engine on Windows 10/11, which draws the standard system accent-colour highlight
        // instead of the colour table's MenuItemSelected value. Painting it directly is the only
        // way to get the mockup's subtle `rgba(255,255,255,.09)` highlight instead of that blue.
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed) return;
            using var brush = new SolidBrush(Highlight);
            e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(Point.Empty, e.ToolStrip.Size);
            rect.Width -= 1;
            rect.Height -= 1;
            using var pen = new Pen(Border);
            using var path = RoundedRectPath(rect, 8);
            e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, 6, y, e.Item.Width - 6, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Text;
            base.OnRenderItemText(e);
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _current?.Dispose();
        if (_currentIconHandle != IntPtr.Zero) NativeMethods.DestroyIcon(_currentIconHandle);
        _current = null;
        _currentIconHandle = IntPtr.Zero;
        _owner.Dispose();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr hIcon);
    }
}
