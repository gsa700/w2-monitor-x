namespace W2.Core;

/// <summary>
/// In Search mode the W2 hops between its two samplers. If the idle sampler catches stray RF, the
/// readout flickers between the real over and the stray reading. This locks the display to the
/// sampler actually carrying RF and rejects frames from the other one until that over ends —
/// "when a sensor is active, ignore the others until it's no longer active."
///
/// Robustness: it locks to the *stronger* sampler (so a stray frame arriving first can't hijack
/// the display — the real over steals the lock), tracks the locked sampler's peak so a voice/PEP
/// trough can't let the stray steal it back, and releases when the locked sampler stops
/// transmitting (or after a long run of unattributable misses). Pure and unit-tested; the meter
/// feeds it one (active sampler, forward power) per cycle.
/// </summary>
public sealed class SensorLock
{
    private readonly double _thresholdW;
    private readonly double _switchMargin;
    private readonly int _releaseAfterMisses;

    private Sampler _locked = Sampler.Unknown;
    private double _lockedPeakW;
    private int _misses;

    public SensorLock(double transmitThresholdW = 0.5, double switchMargin = 1.5, int releaseAfterMisses = 30)
    {
        _thresholdW = transmitThresholdW;
        _switchMargin = switchMargin;
        _releaseAfterMisses = releaseAfterMisses;
    }

    /// <summary>The sampler currently locked, or Unknown when not locked.</summary>
    public Sampler Locked => _locked;

    /// <summary>Feed one cycle. Returns true if the frame should drive the display.</summary>
    public bool Accept(Sampler active, double? forwardW)
    {
        if (active == Sampler.Unknown) return true;   // can't attribute the frame → don't reject it

        var power = forwardW ?? 0.0;
        var transmitting = power > _thresholdW;

        if (_locked == Sampler.Unknown)
        {
            // Lock to the first sampler seen actually transmitting; pass everything through until then.
            if (transmitting) { _locked = active; _lockedPeakW = power; _misses = 0; }
            return true;
        }

        if (active == _locked)
        {
            _misses = 0;
            _lockedPeakW = Math.Max(_lockedPeakW, power);
            if (!transmitting) { _locked = Sampler.Unknown; _lockedPeakW = 0; }   // over ended → release
            return true;
        }

        // A different, identifiable sampler while we're locked.
        if (transmitting && power > _lockedPeakW * _switchMargin)   // real RF clearly moved here
        {
            _locked = active; _lockedPeakW = power; _misses = 0;
            return true;
        }

        // Otherwise it's the stray / idle sampler — ignore it (but bail out of a stuck lock eventually).
        if (++_misses >= _releaseAfterMisses) { _locked = Sampler.Unknown; _lockedPeakW = 0; _misses = 0; }
        return false;
    }

    public void Reset() { _locked = Sampler.Unknown; _lockedPeakW = 0; _misses = 0; }
}
