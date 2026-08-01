namespace W2.Core;

/// <summary>Just the facts the combined-peak rule needs about one meter.</summary>
public readonly record struct MeterPeakState(bool IsConnected, double SessionPeakW);

/// <summary>
/// Pure rule for the peak the <em>single</em> focus window shows, extracted so it can be unit-tested
/// without the UI.
///
/// A dedicated per-meter window shows its own meter's session peak and needs no rule. The focus
/// window is the awkward one: it follows whichever meter is keying, so showing only that meter's
/// peak makes the number appear to jump backwards the moment focus moves to a quieter meter. One
/// window standing in for every meter should report the highest any of them has reached.
/// </summary>
public static class PeakPolicy
{
    /// <summary>
    /// Highest session peak among the <em>connected</em> meters, or zero when none are connected.
    /// </summary>
    /// <remarks>
    /// Disconnected meters are excluded rather than remembered. A peak is only meaningful next to
    /// the meter that measured it, and leaving a vanished meter's number on screen — with nothing
    /// on the focus window naming it — would be a reading attributed to no one. The consequence is
    /// that unplugging the meter holding the maximum lowers the combined figure, which is correct:
    /// it is the peak across what is being measured now, not a high score for the session.
    /// </remarks>
    public static double Combined(IReadOnlyList<MeterPeakState> meters)
    {
        var peak = 0.0;

        foreach (var m in meters)
        {
            if (!m.IsConnected) continue;
            if (m.SessionPeakW > peak) peak = m.SessionPeakW;
        }

        return peak;
    }
}
