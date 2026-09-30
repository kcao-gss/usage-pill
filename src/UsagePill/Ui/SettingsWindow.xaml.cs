using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using UsagePill.Settings;

namespace UsagePill.Ui;

public partial class SettingsWindow : Window
{
    private AppSettings _current;
    private bool _loading = true;
    private readonly List<(RadioButton Swatch, string Hex)> _swatches = new();

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        _current = current;

        RingWeeklyAll.IsChecked = current.Rings.WeeklyAll;
        RingWeeklyPerModel.IsChecked = current.Rings.WeeklyPerModel;
        RingSize.Value = current.RingSizePx;
        Vertical.IsChecked = current.Orientation == PillOrientation.Vertical;
        ResetText.IsChecked = current.ShowResetTimeText;
        OpacitySlider.Value = current.Opacity * 100;
        Interval.Value = current.PollIntervalMinutes;
        Threshold.Value = current.WarnThresholdPercent;
        StartWithWindows.IsChecked = current.StartWithWindows;
        BackgroundRadio(current.Background).IsChecked = true;
        BuildSwatches(current.RingColor);

        _loading = false;
        Hook();
        UpdateLabels();
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>
    /// Reconciles the checkbox with a Start-with-Windows state decided elsewhere (the tray menu
    /// toggle, or the actual Startup folder contents at startup), without republishing it back
    /// out as a fresh SettingsChanged event.
    /// </summary>
    public void SetStartWithWindows(bool enabled)
    {
        _loading = true;
        StartWithWindows.IsChecked = enabled;
        _current = _current with { StartWithWindows = enabled };
        _loading = false;
    }

    private void Hook()
    {
        RingWeeklyAll.Checked += (_, _) => Publish();
        RingWeeklyAll.Unchecked += (_, _) => Publish();
        RingWeeklyPerModel.Checked += (_, _) => Publish();
        RingWeeklyPerModel.Unchecked += (_, _) => Publish();
        Vertical.Checked += (_, _) => Publish();
        Vertical.Unchecked += (_, _) => Publish();
        ResetText.Checked += (_, _) => Publish();
        ResetText.Unchecked += (_, _) => Publish();
        StartWithWindows.Checked += (_, _) => Publish();
        StartWithWindows.Unchecked += (_, _) => Publish();
        RingSize.ValueChanged += (_, _) => Publish();
        OpacitySlider.ValueChanged += (_, _) => Publish();
        Interval.ValueChanged += (_, _) => Publish();
        Threshold.ValueChanged += (_, _) => Publish();
        foreach (var background in Enum.GetValues<BackgroundTheme>())
        {
            BackgroundRadio(background).Checked += (_, _) => Publish();
        }
        foreach (var (swatch, _) in _swatches)
        {
            swatch.Checked += (_, _) => Publish();
        }
    }

    private RadioButton BackgroundRadio(BackgroundTheme background) => background switch
    {
        BackgroundTheme.Dark => BackgroundDark,
        BackgroundTheme.Light => BackgroundLight,
        BackgroundTheme.Amoled => BackgroundAmoled,
        _ => BackgroundSystem,
    };

    private void BuildSwatches(string current)
    {
        var style = (Style)FindResource("Swatch");
        foreach (var (name, hex) in PillTheme.RingSwatches)
        {
            var swatch = new RadioButton
            {
                Style = style,
                Background = new SolidColorBrush(PillTheme.ParseRingColor(hex)),
                ToolTip = name,
                // A hand-written color from settings.json matches no swatch, so none is checked.
                IsChecked = hex == current,
            };
            AutomationProperties.SetName(swatch, name + " rings");
            RingColors.Children.Add(swatch);
            _swatches.Add((swatch, hex));
        }
    }

    private void Publish()
    {
        if (_loading) return;

        _current = _current with
        {
            Rings = new RingSwitches
            {
                Session = true,
                WeeklyAll = RingWeeklyAll.IsChecked == true,
                WeeklyPerModel = RingWeeklyPerModel.IsChecked == true,
            },
            RingSizePx = (int)RingSize.Value,
            Orientation = Vertical.IsChecked == true ? PillOrientation.Vertical : PillOrientation.Horizontal,
            ShowResetTimeText = ResetText.IsChecked == true,
            Opacity = OpacitySlider.Value / 100,
            PollIntervalMinutes = (int)Interval.Value,
            WarnThresholdPercent = (int)Threshold.Value,
            StartWithWindows = StartWithWindows.IsChecked == true,
            Background = Enum.GetValues<BackgroundTheme>().First(b => BackgroundRadio(b).IsChecked == true),
            RingColor = _swatches.FirstOrDefault(s => s.Swatch.IsChecked == true).Hex ?? _current.RingColor,
        };

        UpdateLabels();
        SettingsChanged?.Invoke(this, _current.Normalized());
    }

    private void UpdateLabels()
    {
        RingSizeLabel.Text = ((int)RingSize.Value).ToString(CultureInfo.InvariantCulture) + " px";
        OpacityLabel.Text = ((int)OpacitySlider.Value).ToString(CultureInfo.InvariantCulture) + "%";
        IntervalLabel.Text = ((int)Interval.Value).ToString(CultureInfo.InvariantCulture) + " min";
        ThresholdLabel.Text = ((int)Threshold.Value).ToString(CultureInfo.InvariantCulture) + "%";
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
