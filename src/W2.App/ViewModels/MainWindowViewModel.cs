using Avalonia.Media;
using W2.App.Services;

namespace W2.App.ViewModels;

/// <summary>
/// Phase 3: renders whichever meter the <see cref="MeterManager"/> has in focus. All per-meter
/// state (last-good numerics, TX/peak, the anti-strobe sensor·type·range labels) lives on the
/// meter, so this is a thin formatter that just reflects the focused meter and updates when the
/// manager says focus or its reading changed.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly MeterManager _manager;

    public MainWindowViewModel(MeterManager manager)
    {
        _manager = manager;
        _manager.FocusReadingUpdated += Render;
        _manager.MetersChanged += Render;
        ResetPeakCommand = new RelayCommand(() => _manager.Focus?.ResetPeak());
        Render();
    }

    public string TitleText => "W2 MONITOR";
    public RelayCommand ResetPeakCommand { get; }

    private string _meterNameText = "";
    public string MeterNameText { get => _meterNameText; private set => SetProperty(ref _meterNameText, value); }

    private string _statusText = "No meters — open Setup";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

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
        var m = _manager.Focus;
        if (m is null || !m.IsConnected)
        {
            MeterNameText = m?.Name ?? "";
            StatusText = _manager.Meters.Count == 0 ? "No meters — open Setup" : "Disconnected";
            ConnDotBrush = Palette.DimBrush;
            Blank();
            return;
        }

        MeterNameText = m.Name;
        StatusText = m.StatusIsError ? m.Status : $"{m.Name} · {m.Status}";
        ConnDotBrush = m.StatusIsError ? Palette.RedBrush
            : m.Current is not null ? Palette.GreenBrush : Palette.AmberBrush;

        PowerText = m.LastForwardW is { } f ? $"{f:0.0} W" : "— W";
        PowerBarValue = m.LastForwardW ?? 0;
        PowerBarMax = m.FullScaleW;

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
        StatusLineText = m.Alarm ? "⚠ SWR ALARM" : $"{m.SensorLabel} · {m.TypeLabel} · {m.RangeLabel}";

        TxBrush = m.IsTransmitting ? Palette.AmberBrush : Palette.DimBrush;
        var span = m.TxElapsed;
        TxTimerText = $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    private void Blank()
    {
        PowerText = "— W"; SwrText = "—"; SwrBrush = Palette.DimBrush;
        ReflectedText = "— W"; ReturnLossText = "— dB"; PeakText = "0.0 W";
        PowerBarValue = 0; SwrBarValue = 1.0; StatusLineText = "—"; TxBrush = Palette.DimBrush;
        TxTimerText = "0:00";
    }
}
