using W2.Core;
using Xunit;

namespace W2.Core.Tests;

/// <summary>
/// Round-trips the encoder against the parser: whatever the simulator emits must decode
/// back to what it meant. This keeps W2Wire and W2FrameParser in lock-step.
/// </summary>
public class W2WireTests
{
    [Theory]
    [InlineData('F', 0.0)]
    [InlineData('F', 5.0)]
    [InlineData('F', 123.4)]
    [InlineData('R', 0.5)]
    [InlineData('R', 1999.9)]
    public void Power_round_trips(char cmd, double watts)
    {
        var decoded = W2FrameParser.Power(W2Wire.EncodePower(cmd, watts));
        Assert.Equal(watts, decoded!.Value, 1);
    }

    [Theory]
    [InlineData(1.00)]
    [InlineData(1.53)]
    [InlineData(2.75)]
    [InlineData(9.99)]
    public void Swr_round_trips(double swr)
    {
        var decoded = W2FrameParser.Swr(W2Wire.EncodeSwr(swr));
        Assert.Equal(swr, decoded!.Value, 2);
    }

    [Fact]
    public void Info_round_trips()
    {
        var wire = W2Wire.EncodeInfo(Sampler.S2, rangeKey: '3', autoRange: true, typeKey: '1', leds: true);
        var i = W2FrameParser.Info(wire);
        Assert.True(i.Valid);
        Assert.Equal(Sampler.S2, i.ActiveSampler);
        Assert.Equal("200 W", i.RangeName);
        Assert.Equal(200.0, i.FullScaleW);
        Assert.True(i.AutoRange);
        Assert.Equal("HF 2 kW", i.TypeName);
        Assert.True(i.LedsOn);
    }
}

public class W2SimReaderTests
{
    [Fact]
    public void Emits_valid_readings()
    {
        var readings = new List<W2Reading>();
        using var sim = new W2SimReader();
        sim.ReadingReceived += r => { lock (readings) readings.Add(r); }; // background thread

        sim.Start("SIM");
        Thread.Sleep(300);   // ~3-4 ticks at 80 ms
        sim.Stop();

        lock (readings)
        {
            Assert.NotEmpty(readings);
            // Idle phase: status frame is valid and range decodes to the 200 W full-scale.
            Assert.Contains(readings, r => r.HasStatus && r.FullScaleW == 200.0);
        }
    }
}
