using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using UsagePill.Core;

namespace UsagePill.Ui;

public partial class DetailPopup : Window
{
    private const double ShowGap = 6;

    private static readonly (LimitKind Kind, string Label)[] Order =
    {
        (LimitKind.Session, "Session"),
        (LimitKind.WeeklyAll, "Weekly, all models"),
        (LimitKind.WeeklyScoped, "Weekly, per model"),
    };

    public DetailPopup(int warnThresholdPercent)
    {
        InitializeComponent();
        WarnThresholdPercent = warnThresholdPercent;
    }

    /// <summary>
    /// Read fresh on every <see cref="Apply"/> call, so a threshold change made in the settings
    /// window while this popup is open (or merely constructed) is reflected the next time the
    /// composition root re-renders, without recreating this window.
    /// </summary>
    public int WarnThresholdPercent { get; set; }

    /// <summary>
    /// Set by <see cref="OnDeactivated"/>. Clicking the pill activates PillWindow first, which
    /// deactivates (and hides) this popup before PillWindow.LeftClicked is raised, so a second
    /// click meant to dismiss the popup would otherwise be seen as reopening an already-hidden
    /// one. The composition root uses this timestamp to treat a click that lands immediately
    /// after a deactivation-triggered hide as dismiss-only.
    /// </summary>
    public DateTime LastHiddenAt { get; private set; } = DateTime.MinValue;

    public void Apply(UsageState state, string providerName)
    {
        Body.Children.Clear();
        Body.Children.Add(Header(providerName));

        if (state.Status == UsageStatus.NoCredentials)
        {
            Body.Children.Add(Line("Claude Code not logged in"));
            return;
        }

        if (state.Status == UsageStatus.AuthExpired)
        {
            Body.Children.Add(Line("Login expired - start Claude Code to refresh"));
            return;
        }

        if (state.Status == UsageStatus.Loading || state.Snapshot is null)
        {
            Body.Children.Add(Line("Loading"));
            return;
        }

        var snapshot = state.Snapshot;
        DateTimeOffset? sessionResetsAt = null;
        DateTimeOffset? weeklyResetsAt = null;

        foreach (var (kind, label) in Order)
        {
            var limit = snapshot.Find(kind);
            if (limit is null) continue;

            var name = kind == LimitKind.WeeklyScoped && limit.ScopeLabel is { } scope ? $"Weekly, {scope}" : label;
            var color = RingGeometry.ColorFor(limit.Percent, limit.ApiSeverity, WarnThresholdPercent);
            Body.Children.Add(Row(name, RingGeometry.FormatPercent(limit.Percent) + "%", color));
            Body.Children.Add(Meter(limit, color));

            if (kind == LimitKind.Session) sessionResetsAt = limit.ResetsAt;
            else weeklyResetsAt ??= limit.ResetsAt;
        }

        if (snapshot.Spend is { } spend)
        {
            Body.Children.Add(Row("Extra credits", FormatSpend(spend), RingGeometry.Grey));
        }

        if (sessionResetsAt is not null || weeklyResetsAt is not null)
        {
            Body.Children.Add(Footer(sessionResetsAt, weeklyResetsAt));
        }

        var age = DateTimeOffset.UtcNow - snapshot.CapturedAt;
        Body.Children.Add(Muted(age < TimeSpan.FromMinutes(1) ? "Updated just now" : $"Updated {RingGeometry.FormatResetIn(age)} ago"));

        if (state.Status == UsageStatus.Stale && state.Message is { } message)
        {
            Body.Children.Add(Muted(message));
        }
    }

    public void ShowNear(PillWindow pill)
    {
        // Hidden until repositioned, so a card that must flip above the pill or clamp against a
        // screen edge never flashes at the naive below-the-pill position first.
        Opacity = 0;
        var capsule = pill.CapsuleBounds;
        Left = capsule.Left;
        Top = capsule.Bottom + ShowGap;
        Show();
        Reposition(pill);
        Activate();
        Opacity = 1;
    }

    /// <summary>
    /// Clamps the card to the work area of the monitor under the pill, flipping above the pill
    /// when placing it below would run off the bottom edge (as happens when the pill is bottom-
    /// snapped) and clamping Left when the card - wider than the pill - would run off the right
    /// edge (as happens when the pill is right-snapped). Mirrors the monitor lookup PillWindow
    /// uses for its own edge snapping.
    /// </summary>
    private void Reposition(PillWindow pill)
    {
        var capsule = pill.CapsuleBounds;
        var dpi = VisualTreeHelper.GetDpi(this);
        var pillCentre = new System.Drawing.Point(
            (int)Math.Round((capsule.Left + capsule.Width / 2) * dpi.DpiScaleX),
            (int)Math.Round((capsule.Top + capsule.Height / 2) * dpi.DpiScaleY));
        var bounds = System.Windows.Forms.Screen.FromPoint(pillCentre).WorkingArea;
        var area = new Rect(
            bounds.Left / dpi.DpiScaleX, bounds.Top / dpi.DpiScaleY,
            bounds.Width / dpi.DpiScaleX, bounds.Height / dpi.DpiScaleY);

        var left = capsule.Left;
        var top = capsule.Bottom + ShowGap;

        if (top + ActualHeight > area.Bottom) top = capsule.Top - ActualHeight - ShowGap;
        if (top < area.Top) top = area.Top;

        if (left + ActualWidth > area.Right) left = area.Right - ActualWidth;
        if (left < area.Left) left = area.Left;

        Left = left;
        Top = top;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        LastHiddenAt = DateTime.UtcNow;
        Hide();
    }

    private static string FormatSpend(SpendInfo spend)
    {
        var symbol = spend.Currency == "USD" ? "$" : spend.Currency + " ";
        return $"{symbol}{spend.Used:0.00} / {symbol}{spend.Limit:0}";
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontSize = 14, FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xF1, 0xF6)),
        Margin = new Thickness(0, 0, 0, 8),
    };

    private static UIElement Row(string key, string value, Color chipColor)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var chip = new Ellipse
        {
            Width = 8, Height = 8,
            Fill = new SolidColorBrush(chipColor),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(chip, 0);

        var left = Line(key);
        Grid.SetColumn(left, 1);

        var right = Line(value);
        right.FontWeight = FontWeights.SemiBold;
        Grid.SetColumn(right, 2);

        grid.Children.Add(chip);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private UIElement Meter(UsageLimit limit, Color color)
    {
        var track = new Border
        {
            Height = 4, CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 2, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var fill = new Border
        {
            Height = 4, CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(color),
            Width = Body.Width * Math.Clamp(limit.Percent, 0, 100) / 100,
        };

        track.Child = fill;
        return track;
    }

    private static UIElement Footer(DateTimeOffset? sessionResetsAt, DateTimeOffset? weeklyResetsAt)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

        if (sessionResetsAt is { } session)
        {
            var text = Muted($"Session resets {session.ToLocalTime():HH:mm}");
            text.Margin = new Thickness(0);
            panel.Children.Add(text);
        }

        if (weeklyResetsAt is { } weekly)
        {
            var text = Muted($"Weekly {weekly.ToLocalTime():ddd HH:mm}");
            text.Margin = new Thickness(sessionResetsAt is null ? 0 : 12, 0, 0, 0);
            panel.Children.Add(text);
        }

        return panel;
    }

    private static TextBlock Line(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontSize = 12,
        Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xF1, 0xF6)),
    };

    private static TextBlock Muted(string text)
    {
        var line = Line(text);
        line.FontSize = 11;
        line.Foreground = new SolidColorBrush(Color.FromArgb(0x8C, 0xEE, 0xF1, 0xF6));
        line.Margin = new Thickness(0, 0, 0, 6);
        return line;
    }
}
