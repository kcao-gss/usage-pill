using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using UsagePill.Core;
using UsagePill.Pacing;
using UsagePill.Settings;

namespace UsagePill.Ui;

public partial class PillWindow : Window
{
    private const double ShadowMargin = 22;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int WM_DISPLAYCHANGE = 0x007E;

    private readonly ThemeWatcher _theme;
    private readonly List<(RingId Ring, RingGauge Gauge)> _gauges = new();
    private TextBlock? _resetText;

    private AppSettings _settings;
    private UsageState _state = UsageState.Loading;
    private SpendBudget? _budget;
    private bool _spendOnly;
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
        TaskSwitcher.Exclude(this);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(ClearMinTrackSize);
            source.AddHook(RecoverFromDisplayChange);
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

    // Windows does not reliably move this window when the monitor it sits on is disconnected, so
    // without this the pill stays at coordinates no display covers and is simply gone. Deferred so
    // the handler sees the settled monitor layout rather than the one mid-change.
    private IntPtr RecoverFromDisplayChange(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            Dispatcher.InvokeAsync(RecoverOntoScreen, DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    private void RecoverOntoScreen()
    {
        // The move can carry the pill onto a monitor with a different DPI. WPF rescales it during
        // that move, after the clamp was computed at the old scale, so clamp once more at the new
        // scale to keep it fully inside the work area.
        var dpiBefore = VisualTreeHelper.GetDpi(this).DpiScaleX;
        ConstrainToWorkArea();
        if (VisualTreeHelper.GetDpi(this).DpiScaleX != dpiBefore) ConstrainToWorkArea();
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

    public void Apply(UsageState state, SpendBudget? budget)
    {
        _state = state;
        _budget = budget;

        // Follows the last good snapshot, which the state keeps through errors, so a failed poll
        // never flips the pill between its spend rings and its limit rings.
        var spendOnly = state.Snapshot?.IsSpendOnly == true;
        if (spendOnly != _spendOnly)
        {
            _spendOnly = spendOnly;
            Rebuild();
            return;
        }

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
        var theme = PillTheme.For(_settings.Background, _theme.IsDark);
        Capsule.Background = new SolidColorBrush(theme.CapsuleBackground);
        Capsule.BorderBrush = new SolidColorBrush(theme.CapsuleBorder);

        Rings.Orientation = _settings.Orientation == PillOrientation.Horizontal
            ? Orientation.Horizontal
            : Orientation.Vertical;
        Rings.Children.Clear();
        _gauges.Clear();
        _resetText = null;

        foreach (var ring in RingIds())
        {
            var gauge = new RingGauge
            {
                RingSize = size,
                FontSizePx = RingGeometry.FontSize(size),
                TrackBrush = new SolidColorBrush(theme.RingTrack),
                CoreBrush = new SolidColorBrush(theme.RingCore),
                TextBrush = new SolidColorBrush(theme.RingText),
                Margin = _gauges.Count == 0
                    ? new Thickness(0)
                    : Rings.Orientation == Orientation.Horizontal
                        ? new Thickness(gap, 0, 0, 0)
                        : new Thickness(0, gap, 0, 0),
            };

            Rings.Children.Add(gauge);
            _gauges.Add((ring, gauge));
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
                Foreground = new SolidColorBrush(theme.ResetText),
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

    /// <summary>A spend-only account shows its budget rings, whatever the ring settings say.</summary>
    private IEnumerable<RingId> RingIds()
    {
        if (_spendOnly) return LimitDisplay.SpendRings;
        return LimitDisplay.Order.Where(o => IsRingEnabled(o.Kind)).Select(o => o.Ring);
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
            var (ring, gauge) = _gauges[i];
            var reading = LimitDisplay.Reading(snapshot, _budget, ring);

            gauge.ShowStaleDot = stale && i == 0;

            if (_state.Status is UsageStatus.AuthExpired or UsageStatus.MissingScope && i == 0)
            {
                gauge.Percent = 100;
                gauge.RingColor = RingGeometry.Amber;
                gauge.Text = "!";
                SetTextOpacity(gauge, 1.0);
                continue;
            }

            if (reading is not { } value)
            {
                gauge.Percent = 0;
                gauge.RingColor = RingGeometry.Grey;
                gauge.Text = _state.Status == UsageStatus.Loading ? "--" : "-";
                SetTextOpacity(gauge, 0.5);
                continue;
            }

            gauge.Percent = value.Percent;
            gauge.RingColor = value.ColorFor(_settings.WarnThresholdPercent, PillTheme.ParseRingColor(_settings.RingColor));
            gauge.Text = value.Text;
            SetTextOpacity(gauge, 1.0);
        }

        if (_resetText is not null)
        {
            // A spend cap reports no reset time, so the slot shows what is left of today's share.
            _resetText.Text = _spendOnly
                ? snapshot is not null && _budget is { } budget ? LimitDisplay.FormatLeft(budget.Today, budget.Spend.Currency) : "-"
                : snapshot?.Find(LimitKind.Session)?.ResetsAt is { } resetsAt ? RingGeometry.FormatResetIn(resetsAt - DateTimeOffset.UtcNow) : "-";
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
        if (_state.Status == UsageStatus.MissingScope) return LimitDisplay.MissingScopeMessage;
        if (_state.Status == UsageStatus.Loading) return "Loading";

        var snapshot = _state.Snapshot;
        var text = new StringBuilder();
        foreach (var (_, kind, label) in LimitDisplay.Order)
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

        if (snapshot is { IsSpendOnly: true, Spend: { } spent } && _budget is { } budget)
        {
            AppendPace(text, LimitDisplay.TodayLabel, budget.Today, spent.Currency);
            AppendPace(text, LimitDisplay.WeekLabel, budget.Week, spent.Currency);
            text.Append(budget.MonthName).Append(' ').Append(RingGeometry.FormatPercent(spent.Percent)).Append("% - ")
                .Append(LimitDisplay.FormatSpend(spent)).AppendLine();
        }

        if (_state.Status == UsageStatus.Stale && _state.Message is { } message)
        {
            text.Append(message);
        }

        var tooltip = text.ToString().TrimEnd();
        return tooltip.Length == 0 ? null : tooltip;
    }

    private static void AppendPace(StringBuilder text, string label, SpendPace pace, string currency) =>
        text.Append(label).Append(' ').Append(RingGeometry.FormatPacePercent(pace.Percent)).Append("% - ")
            .Append(LimitDisplay.FormatPace(pace, currency)).AppendLine();

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
        var capsule = new Rect(Left + ShadowMargin, Top + ShadowMargin, Capsule.ActualWidth, Capsule.ActualHeight);
        var area = GetWorkArea(capsule.Left, capsule.Top, capsule.Width, capsule.Height);
        var landed = EdgeSnap.Snap(capsule, area, ShadowMargin);

        Left = landed.X - ShadowMargin;
        Top = landed.Y - ShadowMargin;
    }

    // Called on load, after a display change, and after a settings-driven Rebuild() has grown or
    // shrunk the capsule. Unlike SnapToEdges (which only pulls the capsule to an edge when a drag
    // release lands it near or past one), this always keeps the capsule EdgeSnap.Gap inside the
    // work area: a pill snapped to an edge before the resize stays snapped to it afterwards,
    // while a pill nowhere near an edge is left at its freely chosen position.
    private void ConstrainToWorkArea()
    {
        var capsule = new Rect(Left + ShadowMargin, Top + ShadowMargin, Capsule.ActualWidth, Capsule.ActualHeight);
        var area = GetWorkArea(capsule.Left, capsule.Top, capsule.Width, capsule.Height);
        var clamped = EdgeSnap.Constrain(capsule, area);

        if (Math.Abs(clamped.X - capsule.Left) < 0.5 && Math.Abs(clamped.Y - capsule.Top) < 0.5) return;

        Left = clamped.X - ShadowMargin;
        Top = clamped.Y - ShadowMargin;
    }
}
