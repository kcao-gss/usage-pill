using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UsagePill.Core;
using UsagePill.Settings;

namespace UsagePill.Ui;

public partial class PillWindow : Window
{
    private const double SnapDistance = 16;
    private const double ShadowMargin = 22;

    private static readonly (LimitKind Kind, string Label)[] Order =
    {
        (LimitKind.Session, "Session"),
        (LimitKind.WeeklyAll, "Weekly, all models"),
        (LimitKind.WeeklyScoped, "Weekly, per model"),
    };

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
        Rebuild();
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

        foreach (var (kind, _) in Order)
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
            ConstrainToWorkArea();
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

    private string BuildTooltip()
    {
        if (_state.Status == UsageStatus.NoCredentials) return "Claude Code not logged in";
        if (_state.Status == UsageStatus.AuthExpired) return "Login expired - start Claude Code to refresh";
        if (_state.Status == UsageStatus.Loading) return "Loading";

        var snapshot = _state.Snapshot;
        var text = new StringBuilder();
        foreach (var (kind, label) in Order)
        {
            var limit = snapshot?.Find(kind);
            if (limit is null) continue;

            var name = kind == LimitKind.WeeklyScoped && limit.ScopeLabel is { } scope ? $"Weekly, {scope}" : label;
            text.Append(name).Append(' ').Append(RingGeometry.FormatPercent(limit.Percent)).Append('%');

            if (limit.ResetsAt is { } resets)
            {
                text.Append(" - resets in ").Append(RingGeometry.FormatResetIn(resets - DateTimeOffset.UtcNow));
            }
            text.AppendLine();
        }

        if (_state.Status == UsageStatus.Stale && _state.RetryAt is { } retry)
        {
            text.Append("Stale - retrying at ").Append(retry.ToLocalTime().ToString("HH:mm"));
        }

        return text.ToString().TrimEnd();
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
