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
///
/// <b>Ending the over</b> takes a run of sub-threshold frames, not one. SSB and CW both drop below
/// the transmit floor <i>within</i> an over — between syllables, and between CW elements/words — and
/// releasing on the first such frame handed the display to any stray above the floor on the other
/// sampler, the exact flicker this class exists to prevent. Releasing late costs almost nothing,
/// because the two switch paths above already cover "RF genuinely moved," so the bias is toward
/// holding on. Frames arrive at roughly 4–5/s (measured on real hardware), so the default 4 quiet
/// frames is ~0.8–1 s of continuous silence — comfortably past SSB and CW word gaps, and still
/// inside the 2 s TX hang that <c>MeterService</c> uses to decide an over has ended.
/// </summary>
public sealed class SensorLock
{
    private readonly double _thresholdW;
    private readonly double _switchMargin;
    private readonly int _switchAfter;
    private readonly int _releaseAfter;
    private readonly int _quietAfter;

    private Sampler _locked = Sampler.Unknown;
    private double _lockedPeakW;
    private int _sinceLocked;   // frames since we last saw the locked sampler
    private int _quiet;         // consecutive sub-threshold frames on the locked sampler

    public SensorLock(double transmitThresholdW = 0.5, double switchMargin = 1.5,
        int switchAfterFrames = 3, int releaseAfterFrames = 30, int quietAfterFrames = 4)
    {
        _thresholdW = transmitThresholdW;
        _switchMargin = switchMargin;
        _switchAfter = switchAfterFrames;
        _releaseAfter = releaseAfterFrames;
        _quietAfter = quietAfterFrames;
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
            if (transmitting) Lock(active, power);
            return true;
        }

        if (active == _locked)
        {
            _sinceLocked = 0;
            _lockedPeakW = Math.Max(_lockedPeakW, power);
            // Sub-threshold here is a syllable/element gap until it persists — see the class remarks.
            if (transmitting) _quiet = 0;
            else if (++_quiet >= _quietAfter) Release();   // over really ended
            return true;
        }

        // A different, identifiable sampler while we're locked.
        _sinceLocked++;
        var clearlyStronger = transmitting && power > _lockedPeakW * _switchMargin;   // far hotter → real RF here
        var lockedWentQuiet = transmitting && _sinceLocked >= _switchAfter;           // RF moved to this sampler
        if (clearlyStronger || lockedWentQuiet)
        {
            Lock(active, power);
            return true;   // follow the RF over to this sampler
        }

        if (_sinceLocked >= _releaseAfter)   // fail-safe: haven't seen the locked sampler in a long time
        {
            Release();
            return true;
        }

        return false;   // stray / idle sampler → ignore for display
    }

    public void Reset() => Release();

    private void Lock(Sampler active, double power)
    {
        _locked = active;
        _lockedPeakW = power;
        _sinceLocked = 0;
        _quiet = 0;
    }

    private void Release()
    {
        _locked = Sampler.Unknown;
        _lockedPeakW = 0;
        _sinceLocked = 0;
        _quiet = 0;
    }
}
