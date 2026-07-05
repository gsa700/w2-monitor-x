using Avalonia.Threading;
using W2.Core;

namespace W2.App.Services;

/// <summary>
/// One W2 meter: its reader (real or simulated), identity (id/name/port/chip-serial), latest
/// reading, connection status, and per-meter TX / peak tracking. Marshals the reader's
/// background-thread events onto the UI thread. A <see cref="MeterManager"/> owns a collection
/// of these — this is the per-meter unit the Phase 3 multi-meter path is built on.
/// </summary>
public sealed class MeterService : IDisposable
{
    private const double TxHangSeconds = 2.0;

    private readonly IReadingSource _reader;

    // TX tracking (per meter, so the manager can pick who's transmitting).
    private DateTime _txStart;
    private DateTime _txLast;

    public string Id { get; }
    public string Name { get; set; }
    public string? Port { get; set; }
    public string? Serial { get; set; }   // FTDI/USB chip serial, so the cable is followed across renumbering
    public bool IsSimulated { get; }

    public W2Reading? Current { get; private set; }
    public bool IsConnected { get; private set; }
    public string Status { get; private set; } = "Disconnected";
    public bool StatusIsError { get; private set; }

    public bool IsTransmitting { get; private set; }
    public double SessionPeakW { get; private set; }
    public double OverPeakW { get; private set; }

    // Last-good numerics (hold across per-field serial dropouts).
    public double? LastForwardW { get; private set; }
    public double? LastReflectedW { get; private set; }
    public double? LastSwr { get; private set; }

    // Derived, per-meter display state (anti-strobe: sensor/type/range hold while idle).
    public string SensorLabel { get; private set; } = "—";
    public string TypeLabel { get; private set; } = "—";
    public string RangeLabel { get; private set; } = "—";
    public double FullScaleW { get; private set; } = 200.0;
    public bool Alarm { get; private set; }
    private bool _seeded;

    /// <summary>Elapsed of the current over (live) or the last completed one.</summary>
    public TimeSpan TxElapsed
    {
        get
        {
            if (_txStart == default) return TimeSpan.Zero;
            var span = (IsTransmitting ? DateTime.Now : _txLast) - _txStart;
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }
    }

    /// <summary>Fires on the UI thread for every decoded poll cycle (passes this meter).</summary>
    public event Action<MeterService>? ReadingReceived;

    /// <summary>Fires on the UI thread when this meter's connection/status changes.</summary>
    public event Action<MeterService>? StateChanged;

    public MeterService(string id, string name, bool simulated = false, double simPhaseOffset = 0)
    {
        Id = id;
        Name = name;
        IsSimulated = simulated;
        _reader = simulated ? new W2SimReader(simPhaseOffset) : new SerialReader();

        _reader.ReadingReceived += r => Dispatcher.UIThread.Post(() =>
        {
            Current = r;
            TrackTx(r);
            TrackStatus(r);
            ReadingReceived?.Invoke(this);
        });

        _reader.StatusChanged += (msg, isError) => Dispatcher.UIThread.Post(() =>
        {
            Status = msg;
            StatusIsError = isError;
            if (isError) IsConnected = false;
            StateChanged?.Invoke(this);
        });
    }

    public static string[] GetPortNames() => SerialReader.GetPortNames();

    public void Connect()
    {
        if (Port is null) return;
        Status = $"Connecting {Port}…";
        StatusIsError = false;
        IsConnected = true;
        _reader.Start(Port);
        StateChanged?.Invoke(this);
    }

    public void Disconnect()
    {
        _reader.Stop();
        IsConnected = false;
        IsTransmitting = false;
        Current = null;
        LastForwardW = LastReflectedW = LastSwr = null;
        SensorLabel = TypeLabel = RangeLabel = "—";
        Alarm = false;
        _seeded = false;
        Status = "Disconnected";
        StatusIsError = false;
        StateChanged?.Invoke(this);
    }

    public void ResetPeak() => SessionPeakW = 0;

    private void TrackTx(W2Reading r)
    {
        var now = DateTime.Now;
        if (r.ForwardPowerW is { } fwd) LastForwardW = fwd;
        if (r.ReflectedPowerW is { } refl) LastReflectedW = refl;
        if (r.Swr is { } swr) LastSwr = swr;
        if (r.ForwardPowerW is { } f && f > SessionPeakW) SessionPeakW = f;

        if (r.IsTransmitting)
        {
            if (!IsTransmitting) { IsTransmitting = true; _txStart = now; OverPeakW = 0; }
            _txLast = now;
            if (r.ForwardPowerW is { } pf && pf > OverPeakW) OverPeakW = pf;
        }
        else if (IsTransmitting && (now - _txLast).TotalSeconds > TxHangSeconds)
        {
            IsTransmitting = false;
        }
    }

    /// <summary>
    /// Derive sensor/type/range/full-scale from the I-string, holding last-good while idle so a
    /// Search-mode W2 hopping between samplers doesn't strobe the readout (ports the PS logic).
    /// </summary>
    private void TrackStatus(W2Reading r)
    {
        if (r.HasStatus) FullScaleW = r.FullScaleW;
        Alarm = r.Alarm;

        var rfPresent = r.ForwardPowerW is > 0.1;
        if (r.HasStatus && (rfPresent || !_seeded))
        {
            SensorLabel = r.ActiveSampler switch { Sampler.S1 => "S1", Sampler.S2 => "S2", _ => "—" };
            TypeLabel = r.TypeName ?? "—";
            RangeLabel = r.RangeName ?? "—";
            _seeded = true;
        }
    }

    public void Dispose() => _reader.Dispose();
}
