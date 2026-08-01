using W2.Core;
using Xunit;

namespace W2.Core.Tests;

/// <summary>
/// The peak the single focus window reports. Per-meter windows don't use this — they show their own
/// meter's session peak directly — so every case here is about one window standing in for several
/// meters.
/// </summary>
public class PeakPolicyTests
{
    private static MeterPeakState On(double w) => new(IsConnected: true, SessionPeakW: w);
    private static MeterPeakState Off(double w) => new(IsConnected: false, SessionPeakW: w);

    [Fact]
    public void No_meters_is_zero_rather_than_a_crash()
    {
        Assert.Equal(0.0, PeakPolicy.Combined([]));
    }

    [Fact]
    public void A_single_meter_reports_its_own_peak()
    {
        Assert.Equal(4.6, PeakPolicy.Combined([On(4.6)]));
    }

    [Fact]
    public void The_highest_connected_meter_wins()
    {
        // The point of the rule: the focus window follows whichever meter is keying, so reporting
        // only the focused meter's peak would make the figure drop when focus moved to a quieter one.
        Assert.Equal(11.2, PeakPolicy.Combined([On(4.6), On(11.2), On(0.9)]));
    }

    [Fact]
    public void Order_does_not_matter()
    {
        Assert.Equal(11.2, PeakPolicy.Combined([On(11.2), On(4.6)]));
        Assert.Equal(11.2, PeakPolicy.Combined([On(4.6), On(11.2)]));
    }

    [Fact]
    public void Disconnected_meters_are_ignored_even_when_they_hold_the_maximum()
    {
        // Unplugging the meter that measured the maximum lowers the figure, deliberately: this is
        // the peak across what is being measured now, not a high score for the session.
        Assert.Equal(4.6, PeakPolicy.Combined([Off(150.0), On(4.6)]));
    }

    [Fact]
    public void All_disconnected_is_zero()
    {
        Assert.Equal(0.0, PeakPolicy.Combined([Off(150.0), Off(11.2)]));
    }

    [Fact]
    public void Meters_that_have_seen_nothing_do_not_drag_the_result_down()
    {
        // The shape seen on the CM5 right after a reset: one meter keyed, the other idle at zero.
        Assert.Equal(4.6, PeakPolicy.Combined([On(4.6), On(0.0)]));
    }

    [Fact]
    public void Never_returns_less_than_zero()
    {
        // Session peaks are maxima over forward power and cannot go negative, but the floor is part
        // of the contract rather than an accident of the inputs.
        Assert.Equal(0.0, PeakPolicy.Combined([On(-5.0)]));
    }
}
