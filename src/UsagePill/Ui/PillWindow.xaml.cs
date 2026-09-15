using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using UsagePill.Core;
using UsagePill.Settings;

namespace UsagePill.Ui;

public partial class PillWindow : Window
{
    private const double SnapDistance = 16;
    private const double ShadowMargin = 22;
    private const int WM_GETMINMAXINFO = 0x0024;

    private readonly ThemeWatcher _theme;
    private readonly List<(LimitKind Kind, RingGauge Gauge)> _gauges = new();
    private TextBlock? _resetText;

    private AppSettings _settings;
    private UsageState _state = UsageState.Loading;
    private bool _dragged;

    public PillWindow(AppSettings settings, ThemeWatcher theme)
    {
        InitializeComponent();
        _settings = settings;
        _theme = theme;
        _theme.Changed += OnThemeChanged;
        Capsule.SizeChanged += OnCapsuleSizeChanged;

        Left = settings.Window.Left - ShadowMargin;
        Top = settings.Window.Top - ShadowMargin;
        Loaded += (_, _) =>
        {
            // Re-assert the window's true SizeToContent size once more, now that the source's
            // WM_GETMINMAXINFO hook is attached and every layout/resize pass WPF runs on its own
            // during Show() has already settled. See OnSourceInitialized for why the hook alone
            // cannot fix this at creation time.
            SizeToContent = SizeToContent.Manual;
            SizeToContent = SizeToContent.WidthAndHeight;
            ConstrainToWorkArea();
        };
        Rebuild();
    }

    // Windows enforces its own minimum trackable window size (SM_CXMINTRACK / SM_CYMINTRACK -
    // about 136x39 DIP on a typical desktop) on every top-level window via WM_GETMINMAXINFO,
    // regardless of WindowStyle or ResizeMode. The vertical capsule's natural width (about
    // 88 DIP including the shadow margin) is narrower than that floor, so without intervention
    // the OS silently grows the window back out to it and the capsule renders as a fat rounded
    // rectangle instead of the narrow pill SizeToContent asked for.
    //
    // The very first WM_GETMINMAXINFO for a new window is sent while the native HWND is being
    // created, before OnSourceInitialized runs and before any hook can be attached, so that
    // first clamp cannot be intercepted here - only attach the hook, so it is in place before
    // the Loaded handler forces a corrective resize.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(ClearMinTrackSize);
        }
    }

    private static IntPtr ClearMinTrackSize(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MinTrackSize = new Point32 { X = 1, Y = 1 };
            Marshal.StructureToPtr(info, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point32 Reserved;
        public Point32 MaxSize;
        public Point32 MaxPosition;
        public Point32 MinTrackSize;
        public Point32 MaxTrackSize;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Dispatcher.Invoke(Rebuild);

    private void OnCapsuleSizeChanged(object sender, SizeChangedEventArgs e)
        => Capsule.CornerRadius = new CornerRadius(Math.Min(Capsule.ActualWidth, Capsule.ActualHeight) / 2);

    public event EventHandler<DateTime>? LeftClicked;
    public event EventHandler? RightClicked;

    public WindowPosition CurrentPosition => new() { Left = Left + ShadowMargin, Top = Top + ShadowMargin };

    public Rect CapsuleBounds => new(Left + ShadowMargin, Top + ShadowMargin, Capsule.ActualWidth, Capsule.ActualHeight);

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
        var previousWidth = Capsule.ActualWidth;
        var previousHeight = Capsule.ActualHeight;
        var size = _settings.RingSizePx;
        var gap = RingGeometry.Gap(size);

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

        foreach (var (kind, _) in LimitDisplay.Order)
        {
            if (!IsRingEnabled(kind)) continue;

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
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(_theme.IsDark
                    ? Color.FromRgb(0xEA, 0xEE, 0xF5)
                    : Color.FromRgb(0x16, 0x19, 0x1F)),
                Margin = Rings.Orientation == Orientation.Horizontal
                    ? new Thickness(gap, 0, gap, 0)
                    : new Thickness(0, gap, 0, 0),
            };
            Rings.Children.Add(_resetText);
        }

        Opacity = _settings.Opacity;
        Render();

        if (IsLoaded)
        {
            UpdateLayout();
            if (Capsule.ActualWidth != previousWidth || Capsule.ActualHeight != previousHeight)
            {
                ConstrainToWorkArea();
            }
        }
    }

    private bool IsRingEnabled(LimitKind kind) => kind switch
    {
        LimitKind.Session => true,
        LimitKind.WeeklyAll => _settings.Rings.WeeklyAll,
        LimitKind.WeeklyScoped => _settings.Rings.WeeklyPerModel,
        _ => false,
    };

    private void Render()
    {
        var stale = _state.Status == UsageStatus.Stale;
        var snapshot = _state.Status is UsageStatus.Ok or UsageStatus.Stale ? _state.Snapshot : null;

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
                SetTextOpacity(gauge, 1.0);
                continue;
            }

            if (limit is null)
            {
                gauge.Percent = 0;
                gauge.RingColor = RingGeometry.Grey;
                gauge.Text = _state.Status == UsageStatus.Loading ? "--" : "-";
                SetTextOpacity(gauge, 0.5);
                continue;
            }

            gauge.Percent = limit.Percent;
            gauge.RingColor = RingGeometry.ColorFor(limit.Percent, limit.ApiSeverity, _settings.WarnThresholdPercent);
            gauge.Text = RingGeometry.FormatPercent(limit.Percent);
            SetTextOpacity(gauge, 1.0);
        }

        if (_resetText is not null)
        {
            var resetsAt = snapshot?.Find(LimitKind.Session)?.ResetsAt;
            _resetText.Text = resetsAt is null ? "-" : RingGeometry.FormatResetIn(resetsAt.Value - DateTimeOffset.UtcNow);
        }

        Capsule.Opacity = stale ? 0.6 : 1.0;
        ToolTip = BuildTooltip();
    }

    private static void SetTextOpacity(RingGauge gauge, double opacity)
    {
        if (gauge.TextBrush is SolidColorBrush brush) brush.Opacity = opacity;
    }

    private string? BuildTooltip()
    {
        if (_state.Status == UsageStatus.NoCredentials) return LimitDisplay.NoCredentialsMessage;
        if (_state.Status == UsageStatus.AuthExpired) return LimitDisplay.AuthExpiredMessage;
        if (_state.Status == UsageStatus.Loading) return "Loading";

        var snapshot = _state.Snapshot;
        var text = new StringBuilder();
        foreach (var (kind, label) in LimitDisplay.Order)
        {
            var limit = snapshot?.Find(kind);
            if (limit is null) continue;

            var name = LimitDisplay.NameFor(kind, label, limit.ScopeLabel);
            text.Append(name).Append(' ').Append(RingGeometry.FormatPercent(limit.Percent)).Append('%');

            if (limit.ResetsAt is { } resets)
            {
                text.Append(" - resets in ").Append(RingGeometry.FormatResetIn(resets - DateTimeOffset.UtcNow));
            }
            text.AppendLine();
        }

        if (_state.Status == UsageStatus.Stale && _state.Message is { } message)
        {
            text.Append(message);
        }

        var tooltip = text.ToString().TrimEnd();
        return tooltip.Length == 0 ? null : tooltip;
    }

    private void OnCapsuleMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Captured before DragMove blocks: this is the same moment PillWindow's own activation
        // deactivates (and hides) any open DetailPopup, so the composition root can compare its
        // reopen guard against how long the deactivation-triggered hide preceded this click,
        // not against how long the button happened to stay down before release.
        var mouseDownAt = DateTime.UtcNow;
        _dragged = false;
        var before = new Point(Left, Top);
        DragMove();
        _dragged = Math.Abs(Left - before.X) > 2 || Math.Abs(Top - before.Y) > 2;
        if (_dragged)
        {
            SnapToEdges();
        }
        else
        {
            // DragMove blocks until the button is released and frequently swallows the
            // MouseLeftButtonUp that would normally follow, so the click is raised here
            // instead of waiting on a separate mouse-up handler.
            LeftClicked?.Invoke(this, mouseDownAt);
        }
    }

    private void OnCapsuleRightClick(object sender, MouseButtonEventArgs e)
        => RightClicked?.Invoke(this, EventArgs.Empty);

    private Rect GetWorkArea(double capsuleLeft, double capsuleTop, double capsuleWidth, double capsuleHeight)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var centre = new System.Drawing.Point(
            (int)Math.Round((capsuleLeft + capsuleWidth / 2) * dpi.DpiScaleX),
            (int)Math.Round((capsuleTop + capsuleHeight / 2) * dpi.DpiScaleY));
        var bounds = System.Windows.Forms.Screen.FromPoint(centre).WorkingArea;
        return new Rect(
            bounds.Left / dpi.DpiScaleX,
            bounds.Top / dpi.DpiScaleY,
            bounds.Width / dpi.DpiScaleX,
            bounds.Height / dpi.DpiScaleY);
    }

    private void SnapToEdges()
    {
        var capsuleLeft = Left + ShadowMargin;
        var capsuleTop = Top + ShadowMargin;
        var capsuleWidth = Capsule.ActualWidth;
        var capsuleHeight = Capsule.ActualHeight;
        var area = GetWorkArea(capsuleLeft, capsuleTop, capsuleWidth, capsuleHeight);

        if (Math.Abs(capsuleLeft - area.Left) <= SnapDistance) capsuleLeft = area.Left;
        if (Math.Abs(area.Right - (capsuleLeft + capsuleWidth)) <= SnapDistance) capsuleLeft = area.Right - capsuleWidth;
        if (Math.Abs(capsuleTop - area.Top) <= SnapDistance) capsuleTop = area.Top;
        if (Math.Abs(area.Bottom - (capsuleTop + capsuleHeight)) <= SnapDistance) capsuleTop = area.Bottom - capsuleHeight;

        Left = capsuleLeft - ShadowMargin;
        Top = capsuleTop - ShadowMargin;
    }

    // Called after a settings-driven Rebuild() has grown or shrunk the capsule. Unlike
    // SnapToEdges (which only pulls the capsule flush when a drag release lands it within
    // SnapDistance of an edge), this always keeps the capsule fully inside the work area: a
    // pill that was flush against an edge before the resize stays flush against it afterwards,
    // while a pill nowhere near an edge is left at its freely chosen position.
    private void ConstrainToWorkArea()
    {
        var capsuleLeft = Left + ShadowMargin;
        var capsuleTop = Top + ShadowMargin;
        var capsuleWidth = Capsule.ActualWidth;
        var capsuleHeight = Capsule.ActualHeight;
        var area = GetWorkArea(capsuleLeft, capsuleTop, capsuleWidth, capsuleHeight);

        var minLeft = area.Left;
        var maxLeft = Math.Max(area.Left, area.Right - capsuleWidth);
        var minTop = area.Top;
        var maxTop = Math.Max(area.Top, area.Bottom - capsuleHeight);

        var clampedLeft = Math.Min(Math.Max(capsuleLeft, minLeft), maxLeft);
        var clampedTop = Math.Min(Math.Max(capsuleTop, minTop), maxTop);

        if (Math.Abs(clampedLeft - capsuleLeft) < 0.5 && Math.Abs(clampedTop - capsuleTop) < 0.5) return;

        Left = clampedLeft - ShadowMargin;
        Top = clampedTop - ShadowMargin;
    }
}
