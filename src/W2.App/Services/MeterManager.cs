using System.Collections.ObjectModel;
using System.Linq;
using W2.Core;

namespace W2.App.Services;

/// <summary>
/// Owns every W2 meter and decides which one the main display follows. Auto-focus mirrors the
/// PowerShell app: the transmitting meter wins (highest over-peak if several key at once), it is
/// auto-selected once at the start of its over so a manual pick still sticks, and otherwise the
/// last manual pick — falling back to the first connected meter — holds.
/// </summary>
public sealed class MeterManager : IDisposable
{
    public ObservableCollection<MeterService> Meters { get; } = new();
    public bool IsSimulated { get; }

    private string? _manualFocusId;
    private readonly Dictionary<string, bool> _wasTx = new();
    private int _nextSimOffset;

    public MeterService? Focus { get; private set; }

    /// <summary>Fires when the focused meter's reading updates (drives the live readout).</summary>
    public event Action? FocusReadingUpdated;

    /// <summary>Fires when the meter list, a connection state, or the focus changes.</summary>
    public event Action? MetersChanged;

    public MeterManager(bool simulated = false) => IsSimulated = simulated;

    public string[] AvailablePorts() =>
        IsSimulated ? new[] { "SIM" } : MeterService.GetPortNames();

    public MeterService Add(string name, string? port = null, string? serial = null, string? id = null)
    {
        // In sim mode each meter gets a phase-shifted synthetic scenario so overs alternate.
        var offset = IsSimulated ? (_nextSimOffset += 6) - 6 : 0;
        var meter = new MeterService(id ?? Guid.NewGuid().ToString("N")[..7], name, IsSimulated, offset)
        {
            Port = port,
            Serial = serial,
        };
        meter.ReadingReceived += OnReading;
        meter.StateChanged += OnState;
        _wasTx[meter.Id] = false;
        Meters.Add(meter);
        RecomputeFocus();
        MetersChanged?.Invoke();
        return meter;
    }

    public void Remove(MeterService meter)
    {
        meter.ReadingReceived -= OnReading;
        meter.StateChanged -= OnState;
        meter.Disconnect();
        meter.Dispose();
        _wasTx.Remove(meter.Id);
        Meters.Remove(meter);
        if (_manualFocusId == meter.Id) _manualFocusId = null;
        RecomputeFocus();
        MetersChanged?.Invoke();
    }

    public void ConnectAll() { foreach (var m in Meters) if (m.Port is not null) m.Connect(); }
    public void DisconnectAll() { foreach (var m in Meters) m.Disconnect(); }

    /// <summary>User picked a meter in Setup — pin focus there.</summary>
    public void SetManualFocus(MeterService? meter)
    {
        _manualFocusId = meter?.Id;
        RecomputeFocus();
        MetersChanged?.Invoke();
    }

    private void OnReading(MeterService m)
    {
        NoteOverStart(m);
        RecomputeFocus();
        if (ReferenceEquals(m, Focus)) FocusReadingUpdated?.Invoke();
    }

    private void OnState(MeterService m)
    {
        RecomputeFocus();
        MetersChanged?.Invoke();
    }

    /// <summary>On the false→true TX edge, auto-highlight the meter for this over (once).</summary>
    private void NoteOverStart(MeterService m)
    {
        var was = _wasTx.TryGetValue(m.Id, out var v) && v;
        if (m.IsTransmitting && !was) _manualFocusId = m.Id;
        _wasTx[m.Id] = m.IsTransmitting;
    }

    private void RecomputeFocus()
    {
        var states = Meters
            .Select(m => new MeterFocusState(m.Id, m.IsConnected, m.IsTransmitting, m.OverPeakW))
            .ToList();

        var id = FocusPolicy.Pick(states, _manualFocusId);
        var f = id is null ? null : Meters.FirstOrDefault(m => m.Id == id);

        if (!ReferenceEquals(f, Focus))
        {
            Focus = f;
            MetersChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        foreach (var m in Meters) m.Dispose();
    }
}
