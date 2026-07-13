namespace W2.Core;

/// <summary>
/// A fake W2 for demos and UI work without hardware. Runs the SAME pipeline as the real
/// reader: it builds genuine W2 wire replies with <see cref="W2Wire"/> and decodes them with
/// <see cref="W2FrameParser"/>, so what you see is exactly what the parser produces. Scenario:
/// a repeating ~12 s cycle of idle → a ~6 s transmit "over" (power ramps up, wanders, SWR
/// settles), alternating the active sampler each over, on the 200 W range. ~4% of cycles drop
/// a field to exercise the UI's hold-last-good path.
/// </summary>
public sealed class W2SimReader : IReadingSource
{
    private const int TickMs = 80;

    private readonly Random _rnd = new();
    private readonly double _phaseOffset;
    private Thread? _thread;
    private volatile bool _running;
    private bool _pep;
    private bool _search = true;
    private bool _alarmLock;
    private double _alarmTrip = 2.0;

    /// <param name="phaseOffsetSeconds">Shifts the idle/TX cycle so multiple sims alternate overs.</param>
    public W2SimReader(double phaseOffsetSeconds = 0) => _phaseOffset = phaseOffsetSeconds;

    public event Action<W2Reading>? ReadingReceived;
    public event Action<string, bool>? StatusChanged;

    public bool IsRunning => _running;

    public void Start(string portName, Func<string?>? resolvePort = null)
    {
        Stop();
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "W2-SIM" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _thread?.Join(500); } catch { /* ignore */ }
        _thread = null;
    }

    public void Send(char command)
    {
        switch (command)
        {
            case 'N': _pep = !_pep; break;
            case 'Y': _search = !_search; break;
            case 'A': _alarmLock = !_alarmLock; break;
            case '[': _alarmTrip = Math.Max(1.1, Math.Round(_alarmTrip - 0.1, 1)); break;
            case ']': _alarmTrip = Math.Min(5.0, Math.Round(_alarmTrip + 0.1, 1)); break;
            // 'C' (reset), O/0/1/2/3/L have no visible effect in the sim
        }
    }

    private void Loop()
    {
        StatusChanged?.Invoke("Simulated W2 (demo)", false);
        var start = DateTime.UtcNow;
        var sampler = Sampler.S1;
        var wasTx = false;
        var overCount = 0;

        while (_running)
        {
            var t = (DateTime.UtcNow - start).TotalSeconds + _phaseOffset;
            var cycle = t % 12.0;
            var tx = cycle is >= 3.0 and < 9.0;

            // At the end of each over, flip the active sampler (like Search mode) and count overs.
            if (wasTx && !tx) { sampler = sampler == Sampler.S1 ? Sampler.S2 : Sampler.S1; overCount++; }
            wasTx = tx;

            string f, r, s;
            var swr = 1.0;
            if (tx)
            {
                var into = cycle - 3.0;
                var ramp = Math.Min(1.0, into / 0.5);                 // rise over the first 0.5 s
                var target = 120.0 + 30.0 * Math.Sin(t * 3.0);        // wander ~90–150 W
                var pf = Math.Max(0.0, target * ramp + (_rnd.NextDouble() * 4 - 2));
                // Every third over runs a high SWR so the alarm trips (once it exceeds the trip point).
                swr = overCount % 3 == 0 ? 2.6 + 0.2 * Math.Sin(t * 2.0) : Math.Max(1.0, 1.25 + 0.30 * Math.Sin(t * 0.7));
                var g = (swr - 1.0) / (swr + 1.0);
                f = W2Wire.EncodePower('F', pf);
                r = W2Wire.EncodePower('R', pf * g * g);
                s = W2Wire.EncodeSwr(swr);
            }
            else
            {
                f = W2Wire.EncodePower('F', 0.0);
                r = W2Wire.EncodePower('R', 0.0);
                s = W2Wire.EncodeSwr(1.0);
            }

            // In alarm the W2 returns only "A!;" for the I command; otherwise the normal info string.
            var alarm = tx && swr > _alarmTrip;
            var info = alarm
                ? "A!;"
                : W2Wire.EncodeInfo(sampler, rangeKey: '3' /*200 W*/, autoRange: true, typeKey: '1' /*HF 2 kW*/, leds: true);

            // Occasional serial dropout: a field comes back empty this cycle.
            if (_rnd.NextDouble() < 0.04) { f = ""; r = ""; s = ""; }

            ReadingReceived?.Invoke(W2FrameParser.Build(f, r, s, info)
                with { Pep = _pep, Search = _search, AlarmLock = _alarmLock, AlarmTrip = _alarmTrip });
            Thread.Sleep(TickMs);
        }

        if (!_running) StatusChanged?.Invoke("Disconnected", false);
    }

    public void Dispose() => Stop();
}
