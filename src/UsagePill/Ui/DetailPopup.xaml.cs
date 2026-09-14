using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsagePill.Core;

namespace UsagePill.Ui;

public partial class DetailPopup : Window
{
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

    public void Apply(UsageState state, string providerName)
    {
        Body.Children.Clear();
        Body.Children.Add(Header(providerName));

        if (state.Snapshot is null)
        {
            Body.Children.Add(Line(state.Status == UsageStatus.NoCredentials
                ? "Claude Code not logged in"
                : "Loading"));
            return;
        }

        foreach (var (kind, label) in Order)
        {
            var limit = state.Snapshot.Find(kind);
            if (limit is null) continue;

            var name = kind == LimitKind.WeeklyScoped && limit.ScopeLabel is { } scope ? $"Weekly, {scope}" : label;
            Body.Children.Add(Row(name, RingGeometry.FormatPercent(limit.Percent) + "%"));
            Body.Children.Add(Meter(limit));
            if (limit.ResetsAt is { } resets)
            {
                Body.Children.Add(Muted("Resets in " + RingGeometry.FormatResetIn(resets - DateTimeOffset.UtcNow)));
            }
        }

        if (state.Snapshot.Spend is { } spend)
        {
            Body.Children.Add(Row("Extra credits", $"{spend.Used:0.00} / {spend.Limit:0} {spend.Currency}"));
        }

        var age = DateTimeOffset.UtcNow - state.Snapshot.CapturedAt;
        Body.Children.Add(Muted($"Updated {RingGeometry.FormatResetIn(age)} ago"));

        if (state.Status == UsageStatus.Stale && state.Message is { } message)
        {
            Body.Children.Add(Muted(message));
        }
    }

    public void ShowNear(PillWindow pill)
    {
        Left = pill.Left;
        Top = pill.Top + pill.ActualHeight + 6;
        Show();
        Activate();
    }

    private void OnDeactivated(object? sender, EventArgs e) => Hide();

    private static TextBlock Header(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontSize = 11, FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xEE, 0xF1, 0xF6)),
        Margin = new Thickness(0, 0, 0, 8),
    };

    private static UIElement Row(string key, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = Line(key);
        var right = Line(value);
        right.FontWeight = FontWeights.SemiBold;
        Grid.SetColumn(right, 1);

        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private UIElement Meter(UsageLimit limit)
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
            Background = new SolidColorBrush(RingGeometry.ColorFor(limit.Percent, limit.ApiSeverity, WarnThresholdPercent)),
            Width = 246 * Math.Clamp(limit.Percent, 0, 100) / 100,
        };

        track.Child = fill;
        return track;
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
