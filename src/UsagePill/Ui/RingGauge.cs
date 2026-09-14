using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace UsagePill.Ui;

/// <summary>One ring gauge: a track, an arc, a filled core, and the number.</summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = Register(nameof(Percent), 0d);
    public static readonly DependencyProperty RingColorProperty = Register(nameof(RingColor), RingGeometry.Grey);
    public static readonly DependencyProperty TextProperty = Register(nameof(Text), string.Empty);
    public static readonly DependencyProperty RingSizeProperty = Register(nameof(RingSize), 32d);
    public static readonly DependencyProperty FontSizePxProperty = Register(nameof(FontSizePx), 11d);
    public static readonly DependencyProperty TrackBrushProperty = Register(nameof(TrackBrush), (Brush)Brushes.Gray);
    public static readonly DependencyProperty CoreBrushProperty = Register(nameof(CoreBrush), (Brush)Brushes.Black);
    public static readonly DependencyProperty TextBrushProperty = Register(nameof(TextBrush), (Brush)Brushes.White);
    public static readonly DependencyProperty ShowStaleDotProperty = Register(nameof(ShowStaleDot), false);

    private static DependencyProperty Register<T>(string name, T defaultValue) => DependencyProperty.Register(
        name, typeof(T), typeof(RingGauge),
        new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public Color RingColor { get => (Color)GetValue(RingColorProperty); set => SetValue(RingColorProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double RingSize { get => (double)GetValue(RingSizeProperty); set => SetValue(RingSizeProperty, value); }
    public double FontSizePx { get => (double)GetValue(FontSizePxProperty); set => SetValue(FontSizePxProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush CoreBrush { get => (Brush)GetValue(CoreBrushProperty); set => SetValue(CoreBrushProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public bool ShowStaleDot { get => (bool)GetValue(ShowStaleDotProperty); set => SetValue(ShowStaleDotProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(RingSize, RingSize);

    protected override void OnRender(DrawingContext dc)
    {
        var size = RingSize;
        var thickness = RingGeometry.Thickness((int)size);
        var centre = new Point(size / 2, size / 2);
        var radius = (size - thickness) / 2;

        var trackPen = new Pen(TrackBrush, thickness);
        dc.DrawEllipse(null, trackPen, centre, radius, radius);

        var percent = Math.Clamp(Percent, 0, 100);
        if (percent > 0)
        {
            var arcPen = new Pen(new SolidColorBrush(RingColor), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, arcPen, BuildArc(centre, radius, percent));
        }

        dc.DrawEllipse(CoreBrush, null, centre, radius - thickness / 2, radius - thickness / 2);

        if (!string.IsNullOrEmpty(Text))
        {
            var formatted = new FormattedText(
                Text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                FitFontSize(),
                TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(formatted, new Point(centre.X - formatted.Width / 2, centre.Y - formatted.Height / 2));
        }

        if (ShowStaleDot)
        {
            var dotCentre = new Point(size - 4, 4);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xEA, 0x14, 0x18, 0x1F)), null, dotCentre, 5, 5);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB2)), null, dotCentre, 3.5, 3.5);
        }
    }

    /// <summary>Three digits do not fit at the normal size, so shrink them.</summary>
    private double FitFontSize() => Text.Length >= 3 ? FontSizePx * 0.82 : FontSizePx;

    private static Geometry BuildArc(Point centre, double radius, double percent)
    {
        if (percent >= 100)
        {
            return new EllipseGeometry(centre, radius, radius);
        }

        var angle = percent / 100.0 * 2 * Math.PI;
        var start = new Point(centre.X, centre.Y - radius);
        var end = new Point(
            centre.X + radius * Math.Sin(angle),
            centre.Y - radius * Math.Cos(angle));

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, percent > 50, SweepDirection.Clockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }
}
