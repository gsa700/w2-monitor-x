using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace W2.App.Controls;

/// <summary>
/// The W2's signature readout: a full-width forward-power bar with a thin cyan peak-hold marker
/// riding on it, and a thinner SWR strip below. Custom-drawn (the SmithChartControl pattern from
/// LP-100A) so the peak marker and stacked layout aren't awkward ProgressBar hacks. Either bar
/// can be hidden; the control sizes to whatever is shown.
/// </summary>
public sealed class PowerSwrBar : Control
{
    private const double PowerHeight = 16;
    private const double SwrHeight = 8;
    private const double Gap = 3;
    private const double MarkerWidth = 3;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<PowerSwrBar, double>(nameof(Value));
    public static readonly StyledProperty<double> MaxProperty =
        AvaloniaProperty.Register<PowerSwrBar, double>(nameof(Max), 200.0);
    public static readonly StyledProperty<double> HeldPeakProperty =
        AvaloniaProperty.Register<PowerSwrBar, double>(nameof(HeldPeak));
    public static readonly StyledProperty<double> SwrProperty =
        AvaloniaProperty.Register<PowerSwrBar, double>(nameof(Swr), 1.0);
    public static readonly StyledProperty<bool> ShowPowerBarProperty =
        AvaloniaProperty.Register<PowerSwrBar, bool>(nameof(ShowPowerBar), true);
    public static readonly StyledProperty<bool> ShowSwrBarProperty =
        AvaloniaProperty.Register<PowerSwrBar, bool>(nameof(ShowSwrBar), true);

    static PowerSwrBar()
    {
        AffectsRender<PowerSwrBar>(ValueProperty, MaxProperty, HeldPeakProperty, SwrProperty,
            ShowPowerBarProperty, ShowSwrBarProperty);
        AffectsMeasure<PowerSwrBar>(ShowPowerBarProperty, ShowSwrBarProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Max { get => GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public double HeldPeak { get => GetValue(HeldPeakProperty); set => SetValue(HeldPeakProperty, value); }
    public double Swr { get => GetValue(SwrProperty); set => SetValue(SwrProperty, value); }
    public bool ShowPowerBar { get => GetValue(ShowPowerBarProperty); set => SetValue(ShowPowerBarProperty, value); }
    public bool ShowSwrBar { get => GetValue(ShowSwrBarProperty); set => SetValue(ShowSwrBarProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        double h = 0;
        if (ShowPowerBar) h += PowerHeight;
        if (ShowSwrBar) h += (h > 0 ? Gap : 0) + SwrHeight;
        var w = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(w, h);
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        if (w <= 0) return;
        double y = 0;

        if (ShowPowerBar)
        {
            var track = new Rect(0, y, w, PowerHeight);
            ctx.FillRectangle(Palette.TrackBrush, track, 2);

            var frac = Fraction(Value, Max);
            if (frac > 0)
                ctx.FillRectangle(Palette.BlueBrush, new Rect(0, y, w * frac, PowerHeight), 2);

            // Peak-hold marker: a thin cyan bar at the held-peak position.
            var pk = Fraction(HeldPeak, Max);
            if (pk > 0)
            {
                var x = Math.Min(w - MarkerWidth, Math.Max(0, w * pk - MarkerWidth / 2));
                ctx.FillRectangle(Palette.CyanBrush, new Rect(x, y, MarkerWidth, PowerHeight));
            }
            y += PowerHeight + Gap;
        }

        if (ShowSwrBar)
        {
            var track = new Rect(0, y, w, SwrHeight);
            ctx.FillRectangle(Palette.TrackBrush, track, 2);

            // SWR 1..3 mapped across the bar.
            var frac = Fraction(Swr - 1.0, 2.0);
            if (frac > 0)
                ctx.FillRectangle(Palette.GoldBrush, new Rect(0, y, w * frac, SwrHeight), 2);
        }
    }

    private static double Fraction(double value, double max) =>
        max <= 0 ? 0 : Math.Clamp(value / max, 0, 1);
}
