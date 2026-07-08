namespace W2.Core;

/// <summary>
/// Decides when a serial link has gone dead so the reader can drop it and reconnect. A single
/// missing field is normal (the W2 occasionally skips a reply — held last-good upstream); a run
/// of poll cycles where *nothing* comes back means the device is gone (unplugged, powered off, or
/// renumbered to a new /dev/tty). Pure and clock-free so it unit-tests deterministically — the
/// reader feeds it one bool per cycle and also flags a hard port fault directly.
/// </summary>
public sealed class LinkHealth
{
    private readonly int _threshold;
    private int _consecutiveFailures;

    /// <param name="deadCycleThreshold">
    /// Consecutive all-fields-null cycles before the link is declared lost. At the reader's ~80 ms
    /// poll this is a sub-second grace window, long enough to ride out a transient stall but short
    /// enough to reconnect promptly.
    /// </param>
    public LinkHealth(int deadCycleThreshold = 8)
    {
        if (deadCycleThreshold < 1) deadCycleThreshold = 1;
        _threshold = deadCycleThreshold;
    }

    /// <summary>True once the link is considered lost; stays latched until <see cref="Reset"/>.</summary>
    public bool IsLost { get; private set; }

    /// <summary>Consecutive fully-failed cycles seen so far (for diagnostics/tests).</summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>Record one poll cycle. <paramref name="anyData"/> = at least one field decoded.</summary>
    public void RecordCycle(bool anyData)
    {
        if (anyData)
        {
            _consecutiveFailures = 0;
            IsLost = false;
        }
        else if (++_consecutiveFailures >= _threshold)
        {
            IsLost = true;
        }
    }

    /// <summary>A hard port error (I/O error / port closed): the link is lost immediately.</summary>
    public void Fault() => IsLost = true;

    /// <summary>Clear all state — call after a fresh (re)connect.</summary>
    public void Reset()
    {
        _consecutiveFailures = 0;
        IsLost = false;
    }
}
