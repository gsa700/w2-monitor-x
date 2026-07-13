using W2.Core;
using Xunit;

namespace W2.Core.Tests;

/// <summary>
/// These lock the C# decoders to the on-air-validated PowerShell reference (W2App.ps1).
/// The reply strings are constructed from that app's documented formats/byte map, so a
/// green suite means the port decodes exactly what the proven decoder does. Replace/extend
/// with real device captures when hardware is next available to catch anything the byte
/// map under-specifies (payload bytes b[0]/b[4], edge cases).
/// </summary>
public class W2FrameParserTests
{
    // ---- Power (F/R): [FfRr](\d+)D(\d) -> digits / 10^places ----

    [Theory]
    [InlineData("F1500D1;", 150.0)]
    [InlineData("F0D1;", 0.0)]
    [InlineData("F5D0;", 5.0)]
    [InlineData("R250D2;", 2.5)]
    [InlineData("r1000D3;", 1.0)]
    public void Power_decodes(string reply, double expected) =>
        Assert.Equal(expected, W2FrameParser.Power(reply)!.Value, 3);

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("S150;")]   // an SWR reply is not a power reply
    public void Power_null_on_nonmatch(string reply) =>
        Assert.Null(W2FrameParser.Power(reply));

    // ---- SWR (S): [Ss](\d+) -> digits / 100 ----

    [Theory]
    [InlineData("S100;", 1.00)]
    [InlineData("S150;", 1.50)]
    [InlineData("S999;", 9.99)]
    [InlineData("s205;", 2.05)]
    public void Swr_decodes(string reply, double expected) =>
        Assert.Equal(expected, W2FrameParser.Swr(reply)!.Value, 2);

    [Theory]
    [InlineData("")]
    [InlineData("F150D1;")]
    public void Swr_null_on_nonmatch(string reply) =>
        Assert.Null(W2FrameParser.Swr(reply));

    // ---- SWR-alarm trip point echo: [nn; / ]nn; → nn/10 ----

    [Theory]
    [InlineData("[11;", 1.1)]
    [InlineData("]15;", 1.5)]
    [InlineData("[20;", 2.0)]
    [InlineData("]50;", 5.0)]
    public void AlarmTrip_decodes(string reply, double expected) =>
        Assert.Equal(expected, W2FrameParser.AlarmTrip(reply)!.Value, 1);

    [Theory]
    [InlineData("")]
    [InlineData("S150;")]
    public void AlarmTrip_null_on_nonmatch(string reply) =>
        Assert.Null(W2FrameParser.AlarmTrip(reply));

    // ---- I-string byte map ----
    // Payload (after leading 'I'): [1]=range [2]=auto [3]=type [5]=leds [6]=active.
    // "I0311012;" -> b="0311012": b1='3'(200W) b2='1'(auto) b3='1'(HF 2kW) b5='1'(leds) b6='2'(S2)

    [Fact]
    public void Info_decodes_full_status()
    {
        var i = W2FrameParser.Info("I0311012;");
        Assert.True(i.Valid);
        Assert.False(i.Alarm);
        Assert.Equal("200 W", i.RangeName);
        Assert.Equal(200.0, i.FullScaleW);
        Assert.True(i.AutoRange);
        Assert.Equal("HF 2 kW", i.TypeName);
        Assert.True(i.LedsOn);
        Assert.Equal(Sampler.S2, i.ActiveSampler);
    }

    [Fact]
    public void Info_decodes_2kW_range_and_S1()
    {
        // b="0401001": range='4'(2kW) auto='0' type='1' leds='0' active='1'
        var i = W2FrameParser.Info("I0401001;");
        Assert.Equal("2 kW", i.RangeName);
        Assert.Equal(2000.0, i.FullScaleW);
        Assert.False(i.AutoRange);
        Assert.Equal("HF 2 kW", i.TypeName);
        Assert.False(i.LedsOn);
        Assert.Equal(Sampler.S1, i.ActiveSampler);
    }

    [Fact]
    public void Info_flags_alarm()
    {
        var i = W2FrameParser.Info("A!something;");
        Assert.True(i.Alarm);
        Assert.False(i.Valid);   // alarm frame carries no sensor status
    }

    [Theory]
    [InlineData("")]
    [InlineData("Xnonsense;")]
    public void Info_invalid_on_nonI(string reply)
    {
        var i = W2FrameParser.Info(reply);
        Assert.False(i.Valid);
        Assert.False(i.Alarm);
        Assert.Equal(Sampler.Unknown, i.ActiveSampler);
    }

    [Fact]
    public void Info_strips_nonprintable_leading_bytes()
    {
        // The W2 sometimes prefixes a non-printable byte; the PS app strips 0x00-0x1F/0x7F+.
        var i = W2FrameParser.Info("\x02I0311012;");
        Assert.True(i.Valid);
        Assert.Equal("200 W", i.RangeName);
    }

    // ---- Build: assemble a whole cycle, with per-field dropouts ----

    [Fact]
    public void Build_assembles_all_fields()
    {
        var r = W2FrameParser.Build("F1000D1;", "R50D2;", "S130;", "I0311012;");
        Assert.Equal(100.0, r.ForwardPowerW!.Value, 3);
        Assert.Equal(0.5, r.ReflectedPowerW!.Value, 3);
        Assert.Equal(1.30, r.Swr!.Value, 2);
        Assert.True(r.HasStatus);
        Assert.Equal(Sampler.S2, r.ActiveSampler);
        Assert.True(r.IsTransmitting);
    }

    [Fact]
    public void Build_tolerates_field_dropouts()
    {
        var r = W2FrameParser.Build(null, "", "bad", null);
        Assert.Null(r.ForwardPowerW);
        Assert.Null(r.ReflectedPowerW);
        Assert.Null(r.Swr);
        Assert.False(r.HasStatus);
        Assert.False(r.IsTransmitting);
        Assert.Null(r.ReturnLossDb);
    }

    // ---- Real device captures (W2 A10KMB4VA on COM4, ~15 W carrier into a dummy load, 2026-07-05) ----

    [Fact]
    public void RealCapture_tx_forward_15w() =>
        Assert.Equal(15.53, W2FrameParser.Power("F01553D2;")!.Value, 2);

    [Fact]
    public void RealCapture_tx_reflected() =>
        Assert.Equal(0.03, W2FrameParser.Power("R00003D2;")!.Value, 2);

    [Fact]
    public void RealCapture_tx_swr() =>
        Assert.Equal(1.08, W2FrameParser.Swr("S0108;")!.Value, 2);

    [Fact]
    public void RealCapture_tx_info_locks_to_live_sampler()
    {
        // Under RF the active-sampler byte flips from 0 (hunting) to the live sampler — here S2
        // (byte b[6]='2'), the HF 2 kW input. Idle "I22110101112" has b[6]='0'.
        var i = W2FrameParser.Info("I22110121112;");
        Assert.True(i.Valid);
        Assert.Equal(Sampler.S2, i.ActiveSampler);
        Assert.Equal("HF 2 kW", i.TypeName);
        Assert.Equal("20 W", i.RangeName);
        Assert.Equal(20.0, i.FullScaleW);
    }

    [Fact]
    public void RealCapture_idle_info_is_hunting()
    {
        // Idle Search-mode frame: active = none, so the UI must hold last-good (anti-strobe).
        var i = W2FrameParser.Info("I12120101112;");
        Assert.True(i.Valid);
        Assert.Equal(Sampler.Unknown, i.ActiveSampler);
        Assert.Equal("VHF/UHF", i.TypeName);
    }
}

public class W2ReadingTests
{
    [Fact]
    public void ReturnLoss_is_60dB_at_perfect_match() =>
        Assert.Equal(60.0, new W2Reading { Swr = 1.0 }.ReturnLossDb!.Value, 3);

    [Fact]
    public void ReturnLoss_about_14dB_at_swr_1_5() =>
        Assert.Equal(13.98, new W2Reading { Swr = 1.5 }.ReturnLossDb!.Value, 1);

    [Fact]
    public void ReturnLoss_null_when_swr_unknown() =>
        Assert.Null(new W2Reading { Swr = null }.ReturnLossDb);
}

public class ReplyFramerTests
{
    [Fact]
    public void Splits_multiple_replies_on_semicolon()
    {
        var framer = new ReplyFramer();
        var got = framer.Feed("F150D1;R0D1;S130;");
        Assert.Equal(new[] { "F150D1", "R0D1", "S130" }, got);
    }

    [Fact]
    public void Holds_partial_tail_until_completed()
    {
        var framer = new ReplyFramer();
        Assert.Empty(framer.Feed("S13"));          // no terminator yet
        Assert.Equal(new[] { "S130" }, framer.Feed("0;"));
    }

    [Fact]
    public void Reset_clears_partial()
    {
        var framer = new ReplyFramer();
        framer.Feed("F15");
        framer.Reset();
        Assert.Equal(new[] { "0D1" }, framer.Feed("0D1;"));
    }
}
