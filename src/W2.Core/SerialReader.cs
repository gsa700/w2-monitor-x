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
    private static readonly Regex AEcho = new(@"[Aa]([01])", RegexOptions.Compiled);

    private readonly ConcurrentQueue<char> _cmds = new();
    private readonly ManualResetEventSlim _stop = new(false);  // signalled by Stop(); also wakes backoff waits
    private SerialPort? _port;
    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _linkFaulted;   // set when a query hits a hard port error (device gone)
    private volatile bool _everConnected; // true once a session has connected since Start(): a later
                                          // open failure is a reconnect, not a first-time setup problem
    private int _disposed;                // 0/1 via Interlocked — makes Dispose() idempotent
    private bool? _pep;
    private bool? _search;
    private bool? _alarmLock;      // SWR-alarm locking mode (A command)
    private double? _alarmTrip;    // SWR-alarm trip point 1.1–5.0 ([ / ] commands)

    public event Action<W2Reading>? ReadingReceived;
    public event Action<string, bool>? StatusChanged;  // (message, isError)

    public bool IsRunning => _running;

    public static string[] GetPortNames() => SerialPort.GetPortNames();

    public void Send(char command) => _cmds.Enqueue(command);

    public void Start(string portName, Func<string?>? resolvePort = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Stop();
        _pep = null;
        _search = null;
        _alarmLock = null;
        _alarmTrip = null;
        _everConnected = false;
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
        // Set() happens to tolerate a disposed event today; don't rely on that from a shutdown path.
        try { _stop.Set(); } catch (ObjectDisposedException) { /* nothing left to wake */ }
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
                if (WaitForStop(ReconnectDelayMs)) break;   // Stop() during backoff → exit
            }
        }
        catch (Exception ex)
        {
            // Nothing may escape this thread: an unhandled exception on a background thread tears down
            // the whole process, so a reader fault would take the app with it. The realistic trigger is
            // Stop()'s 3 s join timing out on a wedged session, after which Dispose() disposes _stop
            // while this loop is still going — but a throwing StatusChanged subscriber would do it too.
            Report($"{portName} reader stopped unexpectedly: {ex.Message}", true);
        }
        finally
        {
            ClosePort();
            if (!_running) Report("Disconnected", false);
        }
    }

    private static string? SafeResolve(Func<string?>? resolvePort)
    {
        try { return resolvePort?.Invoke(); } catch { return null; }
    }

    /// <summary>
    /// Raise <see cref="StatusChanged"/> without letting a subscriber's exception escape the reader
    /// thread — see the catch in <see cref="Supervise"/> for why that matters.
    /// </summary>
    private void Report(string message, bool isError)
    {
        try { StatusChanged?.Invoke(message, isError); } catch { /* subscriber's problem, not ours */ }
    }

    /// <summary>
    /// Wait on the stop signal, treating a disposed event as "stop now". <see cref="Stop"/>'s join can
    /// time out on a wedged session and <see cref="Dispose"/> then disposes <c>_stop</c> underneath this
    /// thread; without this the wait would throw and, before the catch above, crash the process.
    /// </summary>
    private bool WaitForStop(int milliseconds)
    {
        try { return _stop.Wait(milliseconds); } catch (ObjectDisposedException) { return true; }
    }

    /// <summary>
    /// Open one port under the <see cref="Guard"/> watchdog, with an explicit ownership handoff so a
    /// slow open can't orphan the handle. If the native <c>Open()</c> outruns the timeout the caller
    /// abandons that thread — but the open may still succeed a moment later, and the resulting port
    /// would then be held by nobody: no field references it, so only the finalizer would ever close
    /// it, and the next reconnect attempt can hit a self-inflicted "port in use." So the opener and the
    /// supervisor race for a single atomic claim, and whichever side loses it closes the port.
    /// </summary>
    private static (bool completed, Exception? error, SerialPort? port) OpenGuarded(string portName)
    {
        SerialPort? handoff = null;
        var claim = 0;   // 0 = unclaimed, 1 = opener published it, 2 = caller abandoned the open

        var (completed, error) = Guard(() =>
        {
            var port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                DtrEnable = true,
                RtsEnable = true,
                ReadTimeout = ReplyTimeoutMs,
                WriteTimeout = ReplyTimeoutMs,
                Encoding = Encoding.ASCII,
            };
            try { port.Open(); }
            catch { port.Dispose(); throw; }   // failed open: nothing to hand off, don't leak the object

            // Publish before claiming: if our claim loses, the caller has already seen `handoff` (its
            // own interlocked op fences the read) and closes it; if it wins, the caller never looks.
            handoff = port;
            if (Interlocked.CompareExchange(ref claim, 1, 0) == 0) return;

            // The caller gave up on us. Nobody is watching this port, so close it here — this thread
            // is already abandoned, so blocking on a removed device's Close() costs nothing.
            handoff = null;
            CloseQuietly(port);
        }, OpenTimeoutMs);

        if (completed) return (true, error, handoff);

        // Timed out. Take the claim so a late-completing open cleans up after itself; if the opener
        // beat us to it, the port is ours and we close it — we've already blown the watchdog budget,
        // so let the supervisor back off and start a fresh session rather than use it.
        if (Interlocked.CompareExchange(ref claim, 2, 0) != 0 && handoff is { } late)
            Guard(() => CloseQuietly(late), CloseTimeoutMs);

        return (false, error, null);
    }

    /// <summary>Close and dispose a port, swallowing anything it throws. Can block if the device is gone.</summary>
    private static void CloseQuietly(SerialPort port)
    {
        try { if (port.IsOpen) port.Close(); } catch { /* ignore */ }
        try { port.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>One connected session: open, poll until the link drops or we're stopped, then close.</summary>
    private void RunSession(string portName)
    {
        _linkFaulted = false;
        var health = new LinkHealth();

        // Open under a watchdog: a healthy FTDI opens in well under a second, but a stale/removed
        // node can block the native call — bound it so a bad port never stalls the reconnect loop.
        var (opened, openError, port) = OpenGuarded(portName);

        if (!opened)
        {
            if (_running) StatusChanged?.Invoke($"{portName} not responding — retrying…", true);
            return;   // abandon the wedged open thread; supervisor backs off and retries
        }
        if (openError is not null)
        {
            if (_running) StatusChanged?.Invoke(DescribeRetry(openError, portName), true);
            return;
        }
        if (port is null) return;   // completed without error but no port (shouldn't happen); retry
        _port = port;

        try
        {
            if (WaitForStop(SettleMs)) return;  // stop requested while settling
            try { port.DiscardInBuffer(); } catch { /* non-fatal */ }
            _everConnected = true;
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
                ReadingReceived?.Invoke(W2FrameParser.Build(f, r, s, i)
                    with { Pep = _pep, Search = _search, AlarmLock = _alarmLock, AlarmTrip = _alarmTrip });
                if (WaitForStop(PollIntervalMs)) break;
            }

            if (_running && (health.IsLost || _linkFaulted))
                StatusChanged?.Invoke($"{portName} lost — reconnecting…", true);
        }
        catch (Exception ex) when (_running)
        {
            StatusChanged?.Invoke(DescribeRetry(ex, portName), true);
        }
        finally
        {
            ClosePort();   // always release the fd — a dropped device must not leave a dangling handle
        }
    }

    /// <summary>
    /// Describe an open/session error for the status line. Once we've connected at least once this
    /// session (<see cref="_everConnected"/>), a transient access error is a mid-replug re-enumeration,
    /// so <see cref="SerialErrors.Describe"/> returns a calm "…reconnecting…" that already implies a
    /// retry — don't double the cue. Anything else keeps the explicit " Retrying…" suffix.
    /// </summary>
    private string DescribeRetry(Exception ex, string portName)
    {
        var msg = SerialErrors.Describe(ex, portName, OperatingSystem.IsLinux(), reconnecting: _everConnected);
        var calm = _everConnected && ex is UnauthorizedAccessException;
        return calm ? msg : msg + " Retrying…";
    }

    /// <summary>Send any queued commands, capturing echoes to track Avg-PEP / Search / alarm state.</summary>
    private void DrainCommands()
    {
        while (_cmds.TryDequeue(out var cmd))
        {
            var reply = Query(cmd);
            if (reply is null) continue;
            switch (cmd)
            {
                case 'N' when NEcho.Match(reply) is { Success: true } n: _pep = n.Groups[1].Value == "P"; break;
                case 'Y' when YEcho.Match(reply) is { Success: true } y: _search = y.Groups[1].Value == "1"; break;
                case 'A' when AEcho.Match(reply) is { Success: true } a: _alarmLock = a.Groups[1].Value == "1"; break;
                case '[' or ']' when W2FrameParser.AlarmTrip(reply) is { } trip: _alarmTrip = trip; break;
            }
        }
    }

    /// <summary>
    /// The W2 has no read-only query for Avg-PEP or Search, so probe by double-toggling each
    /// (read the echoed state, then toggle back — net no change). Skipped while transmitting so a
    /// live reading is never disturbed. Ports W2App.ps1:139-149.
    /// </summary>
    private void ProbeToggleStates()
    {
        // Decode through W2FrameParser rather than a second copy of the F-reply format: it anchors the
        // match and uses TryParse, where the copy that used to live here was unanchored and would throw
        // on an overlong digit run — inside RunSession's try, so a junk frame became a session teardown.
        if (W2FrameParser.Power(Query('F')) is > 0.5) return;

        Query('N');
        if (Query('N') is { } n && NEcho.Match(n) is { Success: true } nm) _pep = nm.Groups[1].Value == "P";
        Query('Y');
        if (Query('Y') is { } y && YEcho.Match(y) is { Success: true } ym) _search = ym.Groups[1].Value == "1";
        Query('A');
        if (Query('A') is { } a && AEcho.Match(a) is { Success: true } am) _alarmLock = am.Groups[1].Value == "1";
        // Read the SWR-alarm trip point with a net-zero nudge (raise then lower); the '[' echo is the
        // restored value (unchanged except a trip already at the 5.0 ceiling, which nets to 4.9).
        Query(']');
        if (Query('[') is { } t && W2FrameParser.AlarmTrip(t) is { } trip) _alarmTrip = trip;
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
        Guard(() => CloseQuietly(port), CloseTimeoutMs);
    }

    public void Dispose()
    {
        // Idempotent by construction. Repeat disposal was harmless before only because _stop's own
        // Dispose() is idempotent — not a property to leave load-bearing as fields get added here.
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Stop();
        _stop.Dispose();
    }
}
