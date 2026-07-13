using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace W2.App.Controls;

/// <summary>
/// The W2's signature readout: a full-width forward-power bar with a thin cyan peak-hold marker
/// riding on it, and a matching (same-height, square) SWR bar below. The SWR bar is coloured green→orange→red across
/// the 1–3 scale; when the meter's alarm trip point is known, red is anchored at the trip (so the
/// bar "goes red where your alarm goes off"). On a live SWR alarm the strip flashes red. Custom-
/// drawn (the SmithChartControl pattern from LP-100A); either bar can be hidden.
/// </summary>
public sealed class PowerSwrBar : Control
{
    private const double PowerHeight = 16;
    private const double SwrHeight = 16;   // match the power bar
    private const double Gap = 3;
    private const double MarkerWidth = 3;
    private const double SwrMin = 1.0;
    private const double SwrMax = 3.0;

    private static readonly Color LowColor = Color.Parse("#3CC850");
    private static readonly Color MidColor = Color.Parse("#FF8C00");
    private static readonly Color HighColor = Color.Parse("#E64C4C");
    private static readonly IBrush AlarmBright = new SolidColorBrush(HighColor);
    private static readonly IBrush AlarmDim = new SolidColorBrush(Color.Parse("#3A1414"));

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<PowerSwrBar, double>(nameof(Value));
    public static readonly StyledProperty<double> MaxProperty =
        AvaloniaProperty.Register<PowerSwrBar, double>(nameof(Max), 200.0);
    public static readonly StyledProperty<double> HeldPeakProperty =
        AvaloniaProperty.Register<PowerSwrBar, double>(nameof(HeldPeak));
    public static readonly StyledProperty<double> SwrProperty =
        AvaloniaProperty.Register<PowerSwrBar, double>(nameof(Swr), 1.0);
    public static readonly StyledProperty<double> AlarmTripProperty =
        AvaloniaProperty.Register<PowerSwrBar, double>(nameof(AlarmTrip));   // 0 = unknown → fixed gradient
    public static readonly StyledProperty<bool> AlarmProperty =
        AvaloniaProperty.Register<PowerSwrBar, bool>(nameof(Alarm));
    public static readonly StyledProperty<bool> ShowPowerBarProperty =
        AvaloniaProperty.Register<PowerSwrBar, bool>(nameof(ShowPowerBar), true);
    public static readonly StyledProperty<bool> ShowSwrBarProperty =
        AvaloniaProperty.Register<PowerSwrBar, bool>(nameof(ShowSwrBar), true);

    /// <summary>Forward-power bar fill; bound to the theme accent so it matches the Setup selection.</summary>
    public static readonly StyledProperty<IBrush> FillProperty =
        AvaloniaProperty.Register<PowerSwrBar, IBrush>(nameof(Fill), Palette.BlueBrush);

    static PowerSwrBar()
    {
        AffectsRender<PowerSwrBar>(ValueProperty, MaxProperty, HeldPeakProperty, SwrProperty,
            AlarmTripProperty, AlarmProperty, ShowPowerBarProperty, ShowSwrBarProperty, FillProperty);
        AffectsMeasure<PowerSwrBar>(ShowPowerBarProperty, ShowSwrBarProperty);
    }

    private readonly DispatcherTimer _flashTimer;
    private bool _flashOn = true;

    public PowerSwrBar()
    {
        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        _flashTimer.Tick += (_, _) => { _flashOn = !_flashOn; InvalidateVisual(); };
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Max { get => GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public double HeldPeak { get => GetValue(HeldPeakProperty); set => SetValue(HeldPeakProperty, value); }
    public double Swr { get => GetValue(SwrProperty); set => SetValue(SwrProperty, value); }
    public double AlarmTrip { get => GetValue(AlarmTripProperty); set => SetValue(AlarmTripProperty, value); }
    public bool Alarm { get => GetValue(AlarmProperty); set => SetValue(AlarmProperty, value); }
    public bool ShowPowerBar { get => GetValue(ShowPowerBarProperty); set => SetValue(ShowPowerBarProperty, value); }
    public bool ShowSwrBar { get => GetValue(ShowSwrBarProperty); set => SetValue(ShowSwrBarProperty, value); }
    public IBrush Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AlarmProperty)
        {
            if (Alarm) { _flashOn = true; _flashTimer.Start(); } else _flashTimer.Stop();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _flashTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

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
            ctx.FillRectangle(Palette.TrackBrush, new Rect(0, y, w, PowerHeight));

            var frac = Fraction(Value, Max);
            if (frac > 0)
                ctx.FillRectangle(Fill ?? Palette.BlueBrush, new Rect(0, y, w * frac, PowerHeight));

            var pk = Fraction(HeldPeak, Max);
            if (pk > 0)
            {
                var x = Math.Min(w - MarkerWidth, Math.Max(0, w * pk - MarkerWidth / 2));
                ctx.FillRectangle(Palette.CyanBrush, new Rect(x, y, MarkerWidth, PowerHeight));
            }
            y += PowerHeight + Gap;
        }

        if (ShowSwrBar)
            RenderSwr(ctx, w, y);
    }

    private void RenderSwr(DrawingContext ctx, double w, double y)
    {
        var strip = new Rect(0, y, w, SwrHeight);

        if (Alarm)
        {
            ctx.FillRectangle(_flashOn ? AlarmBright : AlarmDim, strip);
            return;
        }

        ctx.FillRectangle(Palette.TrackBrush, strip);

        var frac = Fraction(Swr - SwrMin, SwrMax - SwrMin);
        if (frac <= 0) return;

        // Green→orange→red gradient sampled by the fill's leading edge, anchored at the trip point.
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(w, 0, RelativeUnit.Absolute),
        };
        AddSwrStops(brush.GradientStops);
        ctx.FillRectangle(brush, new Rect(0, y, w * frac, SwrHeight));
    }

    private void AddSwrStops(GradientStops stops)
    {
        const double span = SwrMax - SwrMin;
        double Frac(double swr) => Math.Clamp((swr - SwrMin) / span, 0, 1);

        var trip = AlarmTrip;
        if (trip > SwrMin && trip <= SwrMax)
        {
            // Red anchored where the meter's alarm trips; orange approaching; green safely below.
            stops.Add(new GradientStop(LowColor, 0.0));
            stops.Add(new GradientStop(LowColor, Frac(SwrMin + 0.50 * (trip - SwrMin))));
            stops.Add(new GradientStop(MidColor, Frac(SwrMin + 0.82 * (trip - SwrMin))));
            stops.Add(new GradientStop(HighColor, Frac(trip)));
            stops.Add(new GradientStop(HighColor, 1.0));
        }
        else
        {
            stops.Add(new GradientStop(LowColor, 0.0));
            stops.Add(new GradientStop(LowColor, 0.28));   // green through ~SWR 1.5
            stops.Add(new GradientStop(MidColor, 0.55));   // orange around SWR 2.0
            stops.Add(new GradientStop(HighColor, 1.0));   // red toward SWR 3.0
        }
    }

    private static double Fraction(double value, double max) =>
        max <= 0 ? 0 : Math.Clamp(value / max, 0, 1);
}
