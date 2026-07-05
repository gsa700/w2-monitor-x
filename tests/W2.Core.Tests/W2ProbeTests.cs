using W2.Core;
using Xunit;

namespace W2.Core.Tests;

public class W2ProbeTests
{
    [Theory]
    [InlineData("V1.03;")]
    [InlineData("V2;")]
    [InlineData("v1.0;")]
    [InlineData("  V1.03;")]   // leading whitespace tolerated
    public void Recognizes_firmware_reply(string reply) =>
        Assert.True(W2Probe.LooksLikeW2(reply));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("Vabc;")]      // 'V' but not followed by a digit
    [InlineData("F1500D1;")]   // some other command's reply
    public void Rejects_non_firmware(string? reply) =>
        Assert.False(W2Probe.LooksLikeW2(reply));
}
