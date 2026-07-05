using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using W2.App.Services;
using W2.Core;

namespace W2.App.ViewModels;

/// <summary>
/// Phase 2: live single-meter readout. Owns port selection inline (Phase 3 moves that to a
/// Setup window + multi-meter manager). Ports the PowerShell app's two field-tested behaviors:
/// hold-last-good on per-field serial dropouts, and an RF-gated / seed-once refresh of the
/// sensor·type·range status line so idle Search-mode sampler hopping doesn't make it strobe.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private const double TxHangSeconds = 2.0;

    private readonly MeterService _meter;

    private double _sessionPeak;
    private double _lastFullScale = 200.0;
    private bool _seeded;
    private string _sensor = "—", _type = "—", _range = "—";

    private bool _txActive;
    private DateTime _txStart;
    private DateTime _txLast;

    public MainWindowViewModel(MeterService meter)
    {
        _meter = meter;
        _meter.ReadingReceived += OnReading;
        _meter.StateChanged += OnStateChanged;

        ConnectCommand = new RelayCommand(ToggleConnect, () => IsConnected || SelectedPort is not null);
        RefreshCommand = new RelayCommand(RefreshPorts);
        ResetPeakCommand = new RelayCommand(() => { _sessionPeak = 0; PeakText = "0.0 W"; });

        RefreshPorts();
        OnStateChanged();
    }

    public string TitleText => "W2 MONITOR";

    // --- connection / ports ---
    public ObservableCollection<string> Ports { get; } = new();
    public RelayCommand ConnectCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ResetPeakCommand { get; }

    private string? _selectedPort;
    public string? SelectedPort
    {
        get => _selectedPort;
        set { if (SetProperty(ref _selectedPort, value)) ConnectCommand.RaiseCanExecuteChanged(); }
    }

    public bool IsConnected => _meter.IsConnected;
    public string ConnectLabel => IsConnected ? "Disconnect" : "Connect";

    // --- status / header ---
    private string _statusText = "Disconnected";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private IBrush _connDotBrush = Palette.DimBrush;
    public IBrush ConnDotBrush { get => _connDotBrush; private set => SetProperty(ref _connDotBrush, value); }

    private string _statusLineText = "—";
    public string StatusLineText { get => _statusLineText; private set => SetProperty(ref _statusLineText, value); }

    // --- hero readouts ---
    private string _powerText = "— W";
    public string PowerText { get => _powerText; private set => SetProperty(ref _powerText, value); }

    private string _swrText = "—";
    public string SwrText { get => _swrText; private set => SetProperty(ref _swrText, value); }

    private IBrush _swrBrush = Palette.DimBrush;
    public IBrush SwrBrush { get => _swrBrush; private set => SetProperty(ref _swrBrush, value); }

    // --- bars ---
    private double _powerBarValue;
    public double PowerBarValue { get => _powerBarValue; private set => SetProperty(ref _powerBarValue, value); }

    private double _powerBarMax = 200.0;
    public double PowerBarMax { get => _powerBarMax; private set => SetProperty(ref _powerBarMax, value); }

    private double _swrBarValue = 1.0;
    public double SwrBarValue { get => _swrBarValue; private set => SetProperty(ref _swrBarValue, value); }

    // --- secondary rows ---
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

    /// <summary>Pre-select a saved port (called at startup by App).</summary>
    public void SelectPort(string? port)
    {
        if (port is not null && Ports.Contains(port)) SelectedPort = port;
    }

    private void ToggleConnect()
    {
        if (IsConnected) _meter.Disconnect();
        else if (SelectedPort is { } port) _meter.Connect(port);
    }

    private void RefreshPorts()
    {
        var current = SelectedPort;
        Ports.Clear();
        foreach (var p in MeterService.GetPortNames().OrderBy(x => x))
            Ports.Add(p);
        SelectedPort = current is not null && Ports.Contains(current) ? current : Ports.FirstOrDefault();
    }

    private void OnStateChanged()
    {
        StatusText = _meter.Status;
        ConnDotBrush = _meter.StatusIsError ? Palette.RedBrush
            : _meter is { IsConnected: true, Current: not null } ? Palette.GreenBrush
            : _meter.IsConnected ? Palette.AmberBrush : Palette.DimBrush;

        if (!_meter.IsConnected) BlankReadouts();

        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectLabel));
        ConnectCommand.RaiseCanExecuteChanged();
    }

    private void OnReading(W2Reading r)
    {
        ConnDotBrush = Palette.GreenBrush;

        // Power / SWR / derived — hold last-good when a field dropped out this cycle.
        if (r.ForwardPowerW is { } f)
        {
            PowerText = $"{f:0.0} W";
            PowerBarValue = f;
            if (f > _sessionPeak) { _sessionPeak = f; PeakText = $"{f:0.0} W"; }
        }
        if (r.Swr is { } swr)
        {
            SwrText = $"{swr:0.00}";
            SwrBrush = swr < 1.5 ? Palette.GreenBrush : swr < 2.0 ? Palette.AmberBrush : Palette.RedBrush;
            SwrBarValue = Math.Min(3.0, swr);
        }
        if (r.ReflectedPowerW is { } refl) ReflectedText = $"{refl:0.0} W";
        if (r.ReturnLossDb is { } rl) ReturnLossText = $"{rl:0.0} dB";

        // Full-scale (bar max) tracks the meter's range when the status frame is valid.
        if (r.HasStatus) { _lastFullScale = r.FullScaleW; PowerBarMax = r.FullScaleW; }

        // Sensor·type·range: refresh only when RF is present (meter locked to the live sampler)
        // or once to seed a first value; otherwise hold, so idle Search hopping doesn't strobe.
        var rfPresent = r.ForwardPowerW is > 0.1;
        if (r.Alarm)
        {
            StatusLineText = "⚠ SWR ALARM";
        }
        else if (r.HasStatus && (rfPresent || !_seeded))
        {
            _sensor = r.ActiveSampler switch { Sampler.S1 => "S1", Sampler.S2 => "S2", _ => "—" };
            _type = r.TypeName ?? "—";
            _range = r.RangeName ?? "—";
            _seeded = true;
            StatusLineText = $"{_sensor} · {_type} · {_range}";
        }

        TrackTx(r);
    }

    private void TrackTx(W2Reading r)
    {
        var now = DateTime.Now;
        if (r.IsTransmitting)
        {
            if (!_txActive) { _txActive = true; _txStart = now; }
            _txLast = now;
            TxBrush = Palette.AmberBrush;
        }
        else if (_txActive && (now - _txLast).TotalSeconds > TxHangSeconds)
        {
            _txActive = false;
            TxBrush = Palette.DimBrush;
        }

        // Basic elapsed timer. PHASE 6: configurable TOT (yellow 30 s before, red at/after).
        var span = _txActive ? now - _txStart : _txLast - _txStart;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        TxTimerText = $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    private void BlankReadouts()
    {
        PowerText = "— W"; SwrText = "—"; SwrBrush = Palette.DimBrush;
        ReflectedText = "— W"; ReturnLossText = "— dB";
        PowerBarValue = 0; PowerBarMax = _lastFullScale; SwrBarValue = 1.0;
        StatusLineText = "—"; TxBrush = Palette.DimBrush;
        _seeded = false;
    }
}
