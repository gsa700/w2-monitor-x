namespace W2.Core;

/// <summary>
/// In Search mode the W2 hops between its two samplers. This locks the display to the sampler
/// actually carrying RF and rejects frames from the other — "when a sensor is active, ignore the
/// others" — so a little stray RF on the idle sampler doesn't make the readout flicker.
///
/// It distinguishes two situations that look similar frame-to-frame:
///  - <b>Stray during one over:</b> the W2 keeps hunting back to the live sampler, so we keep
///    seeing it — the lock holds and the stray (weaker, and/or interleaved) is ignored.
///  - <b>RF moved to the other sampler</b> (you keyed the other antenna): the W2 locks onto the
///    new sampler and stops visiting the old one, so we stop seeing the locked sampler. When the
///    other sampler is transmitting and the locked one has gone quiet for a few frames, we follow
///    the RF over to it. A far-stronger sampler switches immediately; a long unattributable run
///    releases the lock as a fail-safe. Pure and unit-tested; fed one (sampler, power) per cycle.
/// </summary>
public sealed class SensorLock
{
    private readonly double _thresholdW;
    private readonly double _switchMargin;
    private readonly int _switchAfter;
    private readonly int _releaseAfter;

    private Sampler _locked = Sampler.Unknown;
    private double _lockedPeakW;
    private int _sinceLocked;   // frames since we last saw the locked sampler

    public SensorLock(double transmitThresholdW = 0.5, double switchMargin = 1.5,
        int switchAfterFrames = 3, int releaseAfterFrames = 30)
    {
        _thresholdW = transmitThresholdW;
        _switchMargin = switchMargin;
        _switchAfter = switchAfterFrames;
        _releaseAfter = releaseAfterFrames;
    }

    public Sampler Locked => _locked;

    /// <summary>Feed one cycle. Returns true if the frame should drive the display.</summary>
    public bool Accept(Sampler active, double? forwardW)
    {
        if (active == Sampler.Unknown) return true;   // can't attribute the frame → don't reject it

        var power = forwardW ?? 0.0;
        var transmitting = power > _thresholdW;

        if (_locked == Sampler.Unknown)
        {
            if (transmitting) { _locked = active; _lockedPeakW = power; _sinceLocked = 0; }
            return true;
        }

        if (active == _locked)
        {
            _sinceLocked = 0;
            _lockedPeakW = Math.Max(_lockedPeakW, power);
            if (!transmitting) { _locked = Sampler.Unknown; _lockedPeakW = 0; }   // over ended → release
            return true;
        }

        // A different, identifiable sampler while we're locked.
        _sinceLocked++;
        var clearlyStronger = transmitting && power > _lockedPeakW * _switchMargin;   // far hotter → real RF here
        var lockedWentQuiet = transmitting && _sinceLocked >= _switchAfter;           // RF moved to this sampler
        if (clearlyStronger || lockedWentQuiet)
        {
            _locked = active; _lockedPeakW = power; _sinceLocked = 0;
            return true;   // follow the RF over to this sampler
        }

        if (_sinceLocked >= _releaseAfter)   // fail-safe: haven't seen the locked sampler in a long time
        {
            _locked = Sampler.Unknown; _lockedPeakW = 0; _sinceLocked = 0;
            return true;
        }

        return false;   // stray / idle sampler → ignore for display
    }

    public void Reset() { _locked = Sampler.Unknown; _lockedPeakW = 0; _sinceLocked = 0; }
}
