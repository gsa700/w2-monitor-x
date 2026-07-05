using W2.Core;
using Xunit;

namespace W2.Core.Tests;

public class TxTimerTests
{
    [Fact]
    public void Idle_when_not_transmitting() =>
        Assert.Equal(TxAlert.Idle, TxTimer.Evaluate(transmitting: false, elapsedSeconds: 999, timeoutSec: 180));

    [Fact]
    public void Normal_well_before_timeout() =>
        Assert.Equal(TxAlert.Normal, TxTimer.Evaluate(true, elapsedSeconds: 100, timeoutSec: 180));

    [Fact]
    public void Warning_in_last_30_seconds() =>
        Assert.Equal(TxAlert.Warning, TxTimer.Evaluate(true, elapsedSeconds: 155, timeoutSec: 180));

    [Fact]
    public void Warning_exactly_30_before() =>
        Assert.Equal(TxAlert.Warning, TxTimer.Evaluate(true, elapsedSeconds: 150, timeoutSec: 180));

    [Fact]
    public void Over_at_and_after_timeout()
    {
        Assert.Equal(TxAlert.Over, TxTimer.Evaluate(true, 180, 180));
        Assert.Equal(TxAlert.Over, TxTimer.Evaluate(true, 240, 180));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65, "1:05")]
    [InlineData(600, "10:00")]
    public void Formats_mmss(int seconds, string expected) =>
        Assert.Equal(expected, TxTimer.Format(TimeSpan.FromSeconds(seconds)));
}
