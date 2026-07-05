using System.IO.Ports;
using System.Text;

namespace W2.Core;

/// <summary>
/// Opens one Elecraft W2 (9600 8N1, DTR+RTS asserted) and polls it query/response
/// style: each cycle it asks F (forward), R (reflected), S (SWR), I (info) and raises
/// <see cref="ReadingReceived"/> with the assembled <see cref="W2Reading"/>. UI-agnostic
/// — events fire on a background thread, so subscribers must marshal to their UI thread.
///
/// One reader == one meter. The multi-meter manager (Phase 3) owns a collection of these,
/// mirroring the PowerShell app's per-meter runspaces but with plain background threads.
/// Serial params and the query set come from W2App.ps1 (9600 8N1; DtrEnable/RtsEnable;
/// per-cycle F/R/S/I queries).
/// </summary>
public sealed class SerialReader : IReadingSource
{
    private const int BaudRate = 9600;          // W2App.ps1:134
    private const int PollIntervalMs = 80;      // W2App.ps1:166
    private const int ReplyTimeoutMs = 200;     // W2App.ps1:135 (ReadTimeout)

    private SerialPort? _port;
    private Thread? _thread;
    private volatile bool _running;

    public event Action<W2Reading>? ReadingReceived;
    public event Action<string, bool>? StatusChanged;  // (message, isError)

    public bool IsRunning => _running;

    public static string[] GetPortNames() => SerialPort.GetPortNames();

    public void Start(string portName)
    {
        Stop();
        _running = true;
        _thread = new Thread(() => Loop(portName))
        {
            IsBackground = true,
            Name = $"W2-{portName}",
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _thread?.Join(500); } catch { /* ignore */ }
        _thread = null;
        ClosePort();
    }

    private void Loop(string portName)
    {
        try
        {
            _port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                DtrEnable = true,
                RtsEnable = true,
                ReadTimeout = ReplyTimeoutMs,
                WriteTimeout = ReplyTimeoutMs,
                Encoding = Encoding.ASCII,
            };
            _port.Open();
            Thread.Sleep(120);                 // W2App.ps1:136 — settle after open
            _port.DiscardInBuffer();
            StatusChanged?.Invoke($"Connected on {portName}", false);

            while (_running)
            {
                var f = Query('F');
                var r = Query('R');
                var s = Query('S');
                var i = Query('I');
                ReadingReceived?.Invoke(W2FrameParser.Build(f, r, s, i));
                Thread.Sleep(PollIntervalMs);
            }
        }
        catch (Exception ex) when (_running)
        {
            StatusChanged?.Invoke($"Error: {ex.Message}", true);
        }
        finally
        {
            ClosePort();
            if (!_running) StatusChanged?.Invoke("Disconnected", false);
        }
    }

    /// <summary>Write a single command char and read back one ';'-terminated reply.</summary>
    private string? Query(char cmd)
    {
        if (_port is not { IsOpen: true }) return null;
        try
        {
            _port.DiscardInBuffer();
            _port.Write(cmd.ToString());
            var framer = new ReplyFramer();
            var deadline = DateTime.UtcNow.AddMilliseconds(ReplyTimeoutMs);
            var buffer = new byte[256];
            while (DateTime.UtcNow < deadline)
            {
                var avail = _port.BytesToRead;
                if (avail > 0)
                {
                    var n = _port.Read(buffer, 0, Math.Min(avail, buffer.Length));
                    var replies = framer.Feed(Encoding.ASCII.GetString(buffer, 0, n));
                    if (replies.Count > 0) return replies[0];
                }
                else
                {
                    Thread.Sleep(2);
                }
            }
        }
        catch { /* timeout / transient read error -> null (held last-good upstream) */ }
        return null;
    }

    private void ClosePort()
    {
        try { if (_port is { IsOpen: true }) _port.Close(); }
        catch { /* ignore */ }
        finally
        {
            _port?.Dispose();
            _port = null;
        }
    }

    public void Dispose() => Stop();
}
