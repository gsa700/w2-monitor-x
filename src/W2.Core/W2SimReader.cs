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

    /// <param name="phaseOffsetSeconds">Shifts the idle/TX cycle so multiple sims alternate overs.</param>
    public W2SimReader(double phaseOffsetSeconds = 0) => _phaseOffset = phaseOffsetSeconds;

    public event Action<W2Reading>? ReadingReceived;
    public event Action<string, bool>? StatusChanged;

    public bool IsRunning => _running;

    public void Start(string portName)
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
        if (command == 'N') _pep = !_pep;
        else if (command == 'Y') _search = !_search;
        // other commands (O/0/1/2/3/L) have no visible effect in the sim
    }

    private void Loop()
    {
        StatusChanged?.Invoke("Simulated W2 (demo)", false);
        var start = DateTime.UtcNow;
        var sampler = Sampler.S1;
        var wasTx = false;

        while (_running)
        {
            var t = (DateTime.UtcNow - start).TotalSeconds + _phaseOffset;
            var cycle = t % 12.0;
            var tx = cycle is >= 3.0 and < 9.0;

            // Flip the active sampler at the end of each over, like a Search-mode W2 following RF.
            if (wasTx && !tx) sampler = sampler == Sampler.S1 ? Sampler.S2 : Sampler.S1;
            wasTx = tx;

            var info = W2Wire.EncodeInfo(sampler, rangeKey: '3' /*200 W*/, autoRange: true,
                typeKey: '1' /*HF 2 kW*/, leds: true);

            string f, r, s;
            if (tx)
            {
                var into = cycle - 3.0;
                var ramp = Math.Min(1.0, into / 0.5);                 // rise over the first 0.5 s
                var target = 120.0 + 30.0 * Math.Sin(t * 3.0);        // wander ~90–150 W
                var pf = Math.Max(0.0, target * ramp + (_rnd.NextDouble() * 4 - 2));
                var swr = Math.Max(1.0, 1.25 + 0.30 * Math.Sin(t * 0.7));
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

            // Occasional serial dropout: a field comes back empty this cycle.
            if (_rnd.NextDouble() < 0.04) { f = ""; r = ""; s = ""; }

            ReadingReceived?.Invoke(W2FrameParser.Build(f, r, s, info) with { Pep = _pep, Search = _search });
            Thread.Sleep(TickMs);
        }

        if (!_running) StatusChanged?.Invoke("Disconnected", false);
    }

    public void Dispose() => Stop();
}
