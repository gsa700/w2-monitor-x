using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

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

    private static readonly Regex NEcho = new(@"[Nn]([AP])", RegexOptions.Compiled);
    private static readonly Regex YEcho = new(@"[Yy]([01])", RegexOptions.Compiled);
    private static readonly Regex FwdRf = new(@"[Ff](\d+)D(\d)", RegexOptions.Compiled);

    private readonly ConcurrentQueue<char> _cmds = new();
    private SerialPort? _port;
    private Thread? _thread;
    private volatile bool _running;
    private bool? _pep;
    private bool? _search;

    public event Action<W2Reading>? ReadingReceived;
    public event Action<string, bool>? StatusChanged;  // (message, isError)

    public bool IsRunning => _running;

    public static string[] GetPortNames() => SerialPort.GetPortNames();

    public void Send(char command) => _cmds.Enqueue(command);

    public void Start(string portName)
    {
        Stop();
        _pep = null;
        _search = null;
        while (_cmds.TryDequeue(out _)) { }
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
            ProbeToggleStates();

            while (_running)
            {
                DrainCommands();
                var f = Query('F');
                var r = Query('R');
                var s = Query('S');
                var i = Query('I');
                ReadingReceived?.Invoke(W2FrameParser.Build(f, r, s, i) with { Pep = _pep, Search = _search });
                Thread.Sleep(PollIntervalMs);
            }
        }
        catch (Exception ex) when (_running)
        {
            StatusChanged?.Invoke(SerialErrors.Describe(ex, portName, OperatingSystem.IsLinux()), true);
        }
        finally
        {
            ClosePort();
            if (!_running) StatusChanged?.Invoke("Disconnected", false);
        }
    }

    /// <summary>Send any queued commands, capturing the N/Y echoes to track Avg-PEP / Search.</summary>
    private void DrainCommands()
    {
        while (_cmds.TryDequeue(out var cmd))
        {
            var reply = Query(cmd);
            if (cmd == 'N' && reply is not null && NEcho.Match(reply) is { Success: true } n) _pep = n.Groups[1].Value == "P";
            else if (cmd == 'Y' && reply is not null && YEcho.Match(reply) is { Success: true } y) _search = y.Groups[1].Value == "1";
        }
    }

    /// <summary>
    /// The W2 has no read-only query for Avg-PEP or Search, so probe by double-toggling each
    /// (read the echoed state, then toggle back — net no change). Skipped while transmitting so a
    /// live reading is never disturbed. Ports W2App.ps1:139-149.
    /// </summary>
    private void ProbeToggleStates()
    {
        var fr = Query('F');
        var rf = fr is not null && FwdRf.Match(fr) is { Success: true } m
                 && long.Parse(m.Groups[1].Value) / Math.Pow(10, m.Groups[2].Value[0] - '0') > 0.5;
        if (rf) return;

        Query('N');
        if (Query('N') is { } n && NEcho.Match(n) is { Success: true } nm) _pep = nm.Groups[1].Value == "P";
        Query('Y');
        if (Query('Y') is { } y && YEcho.Match(y) is { Success: true } ym) _search = ym.Groups[1].Value == "1";
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
