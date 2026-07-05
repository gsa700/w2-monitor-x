using W2.Core;
using Xunit;

namespace W2.Core.Tests;

public class FocusPolicyTests
{
    private static MeterFocusState M(string id, bool conn, bool tx, double peak = 0) => new(id, conn, tx, peak);

    [Fact]
    public void Null_when_nothing_connected() =>
        Assert.Null(FocusPolicy.Pick(new[] { M("a", false, false) }, null));

    [Fact]
    public void First_connected_when_idle_and_no_manual() =>
        Assert.Equal("a", FocusPolicy.Pick(new[] { M("a", true, false), M("b", true, false) }, null));

    [Fact]
    public void Transmitting_meter_wins_over_idle()
    {
        var pick = FocusPolicy.Pick(new[] { M("a", true, false), M("b", true, true) }, null);
        Assert.Equal("b", pick);
    }

    [Fact]
    public void Highest_over_peak_wins_when_several_transmit()
    {
        var pick = FocusPolicy.Pick(new[] { M("a", true, true, 50), M("b", true, true, 120) }, null);
        Assert.Equal("b", pick);
    }

    [Fact]
    public void Manual_pick_holds_when_idle()
    {
        var pick = FocusPolicy.Pick(new[] { M("a", true, false), M("b", true, false) }, "b");
        Assert.Equal("b", pick);
    }

    [Fact]
    public void Transmitting_overrides_manual_pick()
    {
        var pick = FocusPolicy.Pick(new[] { M("a", true, true, 30), M("b", true, false) }, "b");
        Assert.Equal("a", pick);
    }

    [Fact]
    public void Falls_back_when_manual_pick_disconnected()
    {
        var pick = FocusPolicy.Pick(new[] { M("a", true, false), M("b", false, false) }, "b");
        Assert.Equal("a", pick);   // b is the manual pick but no longer connected
    }
}
