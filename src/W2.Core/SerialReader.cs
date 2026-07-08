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
///
/// Resilience: the poll runs under a supervisor that detects a dropped device (a hard port
/// I/O error, or <see cref="LinkHealth"/> seeing a run of empty cycles), closes the port so the
/// OS fd is released — never left dangling as "/dev/ttyUSB* (deleted)" — then backs off and
/// reconnects. If a <c>resolvePort</c> delegate is supplied it is re-queried each attempt, so a
/// USB replug/renumber is followed to whatever /dev/tty* the cable now maps to.
/// </summary>
public sealed class SerialReader : IReadingSource
{
    private const int BaudRate = 9600;          // W2App.ps1:134
    private const int PollIntervalMs = 80;      // W2App.ps1:166
    private const int ReplyTimeoutMs = 200;     // W2App.ps1:135 (ReadTimeout)
    private const int SettleMs = 120;           // W2App.ps1:136 — settle after open
    private const int ReconnectDelayMs = 1000;  // backoff between reconnect attempts
    private const int OpenTimeoutMs = 4000;     // cap a native Open() that wedges on a bad device
    private const int CloseTimeoutMs = 1500;    // cap a native Close() that wedges on a removed device

    private static readonly Regex NEcho = new(@"[Nn]([AP])", RegexOptions.Compiled);
    private static readonly Regex YEcho = new(@"[Yy]([01])", RegexOptions.Compiled);
    private static readonly Regex FwdRf = new(@"[Ff](\d+)D(\d)", RegexOptions.Compiled);

    private readonly ConcurrentQueue<char> _cmds = new();
    private readonly ManualResetEventSlim _stop = new(false);  // signalled by Stop(); also wakes backoff waits
    private SerialPort? _port;
    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _linkFaulted;   // set when a query hits a hard port error (device gone)
    private bool? _pep;
    private bool? _search;

    public event Action<W2Reading>? ReadingReceived;
    public event Action<string, bool>? StatusChanged;  // (message, isError)

    public bool IsRunning => _running;

    public static string[] GetPortNames() => SerialPort.GetPortNames();

    public void Send(char command) => _cmds.Enqueue(command);

    public void Start(string portName, Func<string?>? resolvePort = null)
    {
        Stop();
        _pep = null;
        _search = null;
        while (_cmds.TryDequeue(out _)) { }
        _stop.Reset();
        _running = true;
        _thread = new Thread(() => Supervise(portName, resolvePort))
        {
            IsBackground = true,
            Name = $"W2-{portName}",
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _stop.Set();                       // wake any backoff/poll wait immediately
        try { _thread?.Join(3000); } catch { /* ignore */ }
        _thread = null;
        ClosePort();
    }

    /// <summary>
    /// Run <paramref name="action"/> on a throwaway background thread and wait up to
    /// <paramref name="timeoutMs"/>. Returns whether it finished and any exception it threw. This
    /// is the guard around <c>SerialPort.Open()/Close()</c>, which on Linux can block forever when
    /// the FTDI is surprise-removed — if it wedges we abandon that thread (it unblocks once the USB
    /// stack finishes tearing the device down) and let the supervisor get on with reconnecting.
    /// </summary>
    private static (bool completed, Exception? error) Guard(Action action, int timeoutMs)
    {
        Exception? error = null;
        var done = new ManualResetEventSlim(false);
        new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        })
        { IsBackground = true, Name = "W2-io" }.Start();
        return (done.Wait(timeoutMs), error);
    }

    /// <summary>
    /// Outer loop: (re-)resolve the port, run one connected session, and — unless we were asked to
    /// stop — back off and try again. Every session closes its port in a finally, so a dropped
    /// device never leaks an fd, and a replug is picked up by re-querying <paramref name="resolvePort"/>.
    /// </summary>
    private void Supervise(string portName, Func<string?>? resolvePort)
    {
        try
        {
            while (_running)
            {
                var port = SafeResolve(resolvePort) ?? portName;
                RunSession(port);
                if (!_running) break;
                if (_stop.Wait(ReconnectDelayMs)) break;   // Stop() during backoff → exit
            }
        }
        finally
        {
            ClosePort();
            if (!_running) StatusChanged?.Invoke("Disconnected", false);
        }
    }

    private static string? SafeResolve(Func<string?>? resolvePort)
    {
        try { return resolvePort?.Invoke(); } catch { return null; }
    }

    /// <summary>One connected session: open, poll until the link drops or we're stopped, then close.</summary>
    private void RunSession(string portName)
    {
        _linkFaulted = false;
        var health = new LinkHealth();

        // Open under a watchdog: a healthy FTDI opens in well under a second, but a stale/removed
        // node can block the native call — bound it so a bad port never stalls the reconnect loop.
        SerialPort? port = null;
        var (opened, openError) = Guard(() =>
        {
            port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                DtrEnable = true,
                RtsEnable = true,
                ReadTimeout = ReplyTimeoutMs,
                WriteTimeout = ReplyTimeoutMs,
                Encoding = Encoding.ASCII,
            };
            port.Open();
        }, OpenTimeoutMs);

        if (!opened)
        {
            if (_running) StatusChanged?.Invoke($"{portName} not responding — retrying…", true);
            return;   // abandon the wedged open thread; supervisor backs off and retries
        }
        if (openError is not null)
        {
            if (_running) StatusChanged?.Invoke(
                SerialErrors.Describe(openError, portName, OperatingSystem.IsLinux()) + " Retrying…", true);
            return;
        }
        if (port is null) return;   // completed without error but no port (shouldn't happen); retry
        _port = port;

        try
        {
            if (_stop.Wait(SettleMs)) return;  // stop requested while settling
            try { port.DiscardInBuffer(); } catch { /* non-fatal */ }
            StatusChanged?.Invoke($"Connected on {portName}", false);
            ProbeToggleStates();

            while (_running && !_linkFaulted && !health.IsLost)
            {
                DrainCommands();
                var f = Query('F');
                var r = Query('R');
                var s = Query('S');
                var i = Query('I');
                health.RecordCycle(f is not null || r is not null || s is not null || i is not null);
                if (_linkFaulted) health.Fault();
                ReadingReceived?.Invoke(W2FrameParser.Build(f, r, s, i) with { Pep = _pep, Search = _search });
                if (_stop.Wait(PollIntervalMs)) break;
            }

            if (_running && (health.IsLost || _linkFaulted))
                StatusChanged?.Invoke($"{portName} lost — reconnecting…", true);
        }
        catch (Exception ex) when (_running)
        {
            StatusChanged?.Invoke(
                SerialErrors.Describe(ex, portName, OperatingSystem.IsLinux()) + " Retrying…", true);
        }
        finally
        {
            ClosePort();   // always release the fd — a dropped device must not leave a dangling handle
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
        var port = _port;   // snapshot: Stop()/ClosePort() may null the field concurrently
        if (port is not { IsOpen: true }) return null;
        try
        {
            port.DiscardInBuffer();
            port.Write(cmd.ToString());
            var framer = new ReplyFramer();
            var deadline = DateTime.UtcNow.AddMilliseconds(ReplyTimeoutMs);
            var buffer = new byte[256];
            while (DateTime.UtcNow < deadline)
            {
                var avail = port.BytesToRead;
                if (avail > 0)
                {
                    var n = port.Read(buffer, 0, Math.Min(avail, buffer.Length));
                    var replies = framer.Feed(Encoding.ASCII.GetString(buffer, 0, n));
                    if (replies.Count > 0) return replies[0];
                }
                else
                {
                    Thread.Sleep(2);
                }
            }
        }
        catch (Exception ex)
        {
            // A hard port error (device unplugged / port closed) means the link is gone — flag it so
            // the session tears down and reconnects. A plain timeout doesn't throw here (the loop just
            // hits its deadline and returns null → held last-good upstream), so anything caught is fatal.
            if (ex is IOException or ObjectDisposedException or InvalidOperationException or UnauthorizedAccessException)
                _linkFaulted = true;
        }
        return null;
    }

    private void ClosePort()
    {
        var port = Interlocked.Exchange(ref _port, null);   // one closer wins; Query sees null next
        if (port is null) return;
        // Close under a watchdog: on Linux a surprise-removed FTDI can make Close()/Dispose() block
        // forever. If it wedges we abandon that thread (background — it unblocks once the device is
        // fully gone) rather than let it freeze reconnect or Stop().
        Guard(() =>
        {
            try { if (port.IsOpen) port.Close(); } catch { /* ignore */ }
            port.Dispose();
        }, CloseTimeoutMs);
    }

    public void Dispose()
    {
        Stop();
        _stop.Dispose();
    }
}
