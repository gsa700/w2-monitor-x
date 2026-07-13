using System.Reflection;
using Avalonia.Media;
using Avalonia.Threading;
using W2.App.Services;
using W2.App.Settings;
using W2.Core;

namespace W2.App.ViewModels;

/// <summary>
/// Renders one W2's readout. Two modes: <b>focus</b> (follows whichever meter the
/// <see cref="MeterManager"/> has focused — the default single window) and <b>pinned</b>
/// (always shows one specific <see cref="MeterService"/> — a dedicated per-meter window).
/// Per-meter state lives on the meter, so this is a thin formatter. A 500 ms tick keeps the TX
/// timer counting and drives the flashing over-time state. Dispose unsubscribes + stops the tick
/// so per-meter windows can open and close cleanly.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly MeterManager? _manager;   // focus mode
    private readonly MeterService? _pinned;    // pinned mode
    private readonly DispatcherTimer _tick;
    private bool _flashOn;

    /// <summary>Focus mode — follows the manager's focused meter.</summary>
    public MainWindowViewModel(MeterManager manager, DisplaySettings display) : this(display)
    {
        _manager = manager;
        _manager.FocusReadingUpdated += Render;
        _manager.MetersChanged += Render;
        Render();
    }

    /// <summary>Pinned mode — always shows one meter (a dedicated window).</summary>
    public MainWindowViewModel(MeterService meter, DisplaySettings display) : this(display)
    {
        _pinned = meter;
        _pinned.ReadingReceived += OnMeter;
        _pinned.StateChanged += OnMeter;
        Render();
    }

    private MainWindowViewModel(DisplaySettings display)
    {
        Display = display;
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _tick.Tick += (_, _) => { _flashOn = !_flashOn; UpdateTx(); };
        _tick.Start();
    }

    private void OnMeter(MeterService _) => Render();

    /// <summary>The meter this window is currently showing.</summary>
    private MeterService? Target => _pinned ?? _manager?.Focus;

    public DisplaySettings Display { get; }

    // Header: the app name in focus mode, or the meter's name in a dedicated window.
    public string TitleText => _pinned is { } m ? m.Name.ToUpperInvariant() : "W2 MONITOR";

    // Title bar: "W2 Monitor vX" or "W2 #1 — W2 Monitor vX".
    public string WindowTitle
    {
        get
        {
            var v = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            var plus = v.IndexOf('+');
            if (plus >= 0) v = v[..plus];
            var app = string.IsNullOrEmpty(v) ? "W2 Monitor" : $"W2 Monitor v{v}";
            return _pinned is { } m ? $"{m.Name} — {app}" : app;
        }
    }

    private IBrush _connDotBrush = Palette.DimBrush;
    public IBrush ConnDotBrush { get => _connDotBrush; private set => SetProperty(ref _connDotBrush, value); }

    private string _statusLineText = "—";
    public string StatusLineText { get => _statusLineText; private set => SetProperty(ref _statusLineText, value); }

    private string _powerText = "— W";
    public string PowerText { get => _powerText; private set => SetProperty(ref _powerText, value); }

    private string _swrText = "—";
    public string SwrText { get => _swrText; private set => SetProperty(ref _swrText, value); }

    private IBrush _swrBrush = Palette.DimBrush;
    public IBrush SwrBrush { get => _swrBrush; private set => SetProperty(ref _swrBrush, value); }

    private double _powerBarValue;
    public double PowerBarValue { get => _powerBarValue; private set => SetProperty(ref _powerBarValue, value); }

    private double _powerBarMax = 200.0;
    public double PowerBarMax { get => _powerBarMax; private set => SetProperty(ref _powerBarMax, value); }

    private double _swrBarValue = 1.0;
    public double SwrBarValue { get => _swrBarValue; private set => SetProperty(ref _swrBarValue, value); }

    private double _heldPeakValue;
    public double HeldPeakValue { get => _heldPeakValue; private set => SetProperty(ref _heldPeakValue, value); }

    private string _reflectedText = "— W";
    public string ReflectedText { get => _reflectedText; private set => SetProperty(ref _reflectedText, value); }

    private string _returnLossText = "— dB";
    public string ReturnLossText { get => _returnLossText; private set => SetProperty(ref _returnLossText, value); }

    private string _peakText = "0.0 W";
    public string PeakText { get => _peakText; private set => SetProperty(ref _peakText, value); }

    private string _txTimerText = "0:00";
    public string TxTimerText { get => _txTimerText; private set => SetProperty(ref _txTimerText, value); }

    private IBrush _txBrush = Palette.DimBrush;
    public IBrush TxBrush { get => _txBrush; private set => SetProperty(ref _txBrush, value); }

    private void Render()
    {
        var m = Target;
        if (m is null || !m.IsConnected)
        {
            ConnDotBrush = Palette.DimBrush;
            Blank();
            StatusLineText = _pinned is null && _manager!.Meters.Count == 0 ? "No meters — open Setup" : "Disconnected";
            return;
        }

        ConnDotBrush = m.StatusIsError ? Palette.RedBrush
            : m.Current is not null ? Palette.GreenBrush : Palette.AmberBrush;

        PowerText = m.LastForwardW is { } f ? $"{f:0.0} W" : "— W";
        PowerBarValue = m.LastForwardW ?? 0;
        PowerBarMax = m.FullScaleW;
        HeldPeakValue = m.HeldPeakW;

        if (m.LastSwr is { } swr)
        {
            SwrText = $"{swr:0.00}";
            SwrBrush = swr < 1.5 ? Palette.GreenBrush : swr < 2.0 ? Palette.AmberBrush : Palette.RedBrush;
            SwrBarValue = Math.Min(3.0, swr);
        }
        else { SwrText = "—"; SwrBrush = Palette.DimBrush; SwrBarValue = 1.0; }

        ReflectedText = m.LastReflectedW is { } refl ? $"{refl:0.0} W" : "— W";
        ReturnLossText = m.Current?.ReturnLossDb is { } rl ? $"{rl:0.0} dB" : "— dB";
        PeakText = $"{m.SessionPeakW:0.0} W";

        // Line 2: sensor · type · range. A dedicated window names its meter in the title, so it
        // doesn't need the prefix; the focus window prefixes the in-use meter when >1 is connected.
        var prefix = _pinned is null && _manager!.Meters.Count(x => x.IsConnected) > 1 ? $"{m.Name} · " : "";
        StatusLineText = m.Alarm ? "⚠ SWR ALARM" : $"{prefix}{m.SensorLabel} · {m.TypeLabel} · {m.RangeLabel}";

        UpdateTx();
    }

    /// <summary>TX timer text + color, incl. the yellow→flashing-red timeout states.</summary>
    private void UpdateTx()
    {
        var m = Target;
        if (m is null || !m.IsConnected) { TxTimerText = "0:00"; TxBrush = Palette.DimBrush; return; }

        TxTimerText = TxTimer.Format(m.TxElapsed);
        var alert = TxTimer.Evaluate(m.IsTransmitting, m.TxElapsed.TotalSeconds, Display.TimeoutSec);
        TxBrush = alert switch
        {
            TxAlert.Over => _flashOn ? Palette.RedBrush : Palette.DimBrush,   // flashing red at/after TOT
            TxAlert.Warning => Palette.AmberBrush,                            // solid yellow, last 30 s
            TxAlert.Normal => Palette.GreenBrush,
            _ => Palette.DimBrush,
        };
    }

    private void Blank()
    {
        PowerText = "— W"; SwrText = "—"; SwrBrush = Palette.DimBrush;
        ReflectedText = "— W"; ReturnLossText = "— dB"; PeakText = "0.0 W";
        PowerBarValue = 0; SwrBarValue = 1.0; HeldPeakValue = 0; StatusLineText = "—"; TxBrush = Palette.DimBrush;
        TxTimerText = "0:00";
    }

    public void Dispose()
    {
        _tick.Stop();
        if (_manager is not null) { _manager.FocusReadingUpdated -= Render; _manager.MetersChanged -= Render; }
        if (_pinned is not null) { _pinned.ReadingReceived -= OnMeter; _pinned.StateChanged -= OnMeter; }
    }
}
