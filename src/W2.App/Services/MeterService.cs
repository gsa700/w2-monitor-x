using Avalonia.Threading;
using W2.Core;

namespace W2.App.Services;

/// <summary>
/// Single shared owner of one W2 connection. Wraps <see cref="SerialReader"/>, marshals its
/// background-thread events onto the UI thread, and re-broadcasts them. Mirrors the LP-100A
/// MeterService — deliberately single-connection for Phase 2. PHASE 3 replaces this with a
/// MeterManager owning a collection of readers (one per W2), with auto-focus.
/// </summary>
public sealed class MeterService : IDisposable
{
    private readonly IReadingSource _reader;

    /// <summary>True when driven by the synthetic <see cref="W2SimReader"/> instead of a real port.</summary>
    public bool IsSimulated { get; }

    public W2Reading? Current { get; private set; }
    public bool IsConnected { get; private set; }
    public string? CurrentPort { get; private set; }
    public string Status { get; private set; } = "Disconnected";
    public bool StatusIsError { get; private set; }

    /// <summary>Fires on the UI thread for every decoded poll cycle.</summary>
    public event Action<W2Reading>? ReadingReceived;

    /// <summary>Fires on the UI thread when connection/status changes.</summary>
    public event Action? StateChanged;

    public MeterService(bool simulated = false)
    {
        IsSimulated = simulated;
        _reader = simulated ? new W2SimReader() : new SerialReader();

        _reader.ReadingReceived += r => Dispatcher.UIThread.Post(() =>
        {
            Current = r;
            ReadingReceived?.Invoke(r);
        });

        _reader.StatusChanged += (msg, isError) => Dispatcher.UIThread.Post(() =>
        {
            Status = msg;
            StatusIsError = isError;
            if (isError) IsConnected = false;
            StateChanged?.Invoke();
        });
    }

    public static string[] GetPortNames() => SerialReader.GetPortNames();

    /// <summary>Ports offered in the UI: the synthetic "SIM" port in simulator mode, else real COM ports.</summary>
    public string[] AvailablePorts() => IsSimulated ? new[] { "SIM" } : GetPortNames();

    public void Connect(string port)
    {
        CurrentPort = port;
        Status = $"Connecting {port}…";
        StatusIsError = false;
        IsConnected = true;
        _reader.Start(port);
        StateChanged?.Invoke();
    }

    public void Disconnect()
    {
        _reader.Stop();
        IsConnected = false;
        Current = null;
        Status = "Disconnected";
        StatusIsError = false;
        StateChanged?.Invoke();
    }

    public void Dispose() => _reader.Dispose();
}
