using W2.Core;
using Xunit;

namespace W2.Core.Tests;

/// <summary>
/// The <c>V</c> reply, per Elecraft's Serial Interface Commands Rev D: <c>Vn.nn;</c>, version 0.01 to
/// 9.99, marked "EEPROM: No" — a pure query, which is why the reader can issue it on every connect.
/// </summary>
public class FirmwareParseTests
{
    [Theory]
    [InlineData("V1.03;", "1.03")]
    [InlineData("v1.03;", "1.03")]     // the manual documents either case
    [InlineData("V0.01;", "0.01")]     // documented floor
    [InlineData("V9.99;", "9.99")]     // documented ceiling
    [InlineData("V1.03", "1.03")]      // terminator already stripped by the framer
    public void ReadsTheDocumentedForm(string reply, string expected) =>
        Assert.Equal(expected, W2FrameParser.Firmware(reply));

    [Fact]
    public void LeadingWhitespaceIsToleratedTheWayTheProbeToleratesIt()
    {
        // W2Probe.LooksLikeW2 trims before matching, so a reply that arrives with a stray leading
        // byte is still recognised as a W2. The parser has to agree, or a meter that passes Detect
        // would show a blank firmware.
        Assert.Equal("1.03", W2FrameParser.Firmware("  V1.03;"));
    }

    [Fact]
    public void AcceptsAVersionOutsideTheDocumented2010Range()
    {
        // Rev D dates from 2010 and describes n.nn. A later firmware numbering itself 10.2 should
        // appear in Setup as what it says rather than vanish because the pattern was written to a
        // fifteen-year-old document.
        Assert.Equal("10.2", W2FrameParser.Firmware("V10.2;"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I2314001;")]          // an info frame, not a version
    [InlineData("V;")]                 // V with no digits
    [InlineData("Vx.yz;")]
    [InlineData("1.03")]               // no V at all
    [InlineData("xV1.03;")]            // must be anchored: V has to lead
    public void AnythingElseIsNull(string? reply) =>
        Assert.Null(W2FrameParser.Firmware(reply));

    [Fact]
    public void AgreesWithTheProbeAboutWhatCountsAsAW2()
    {
        // Detect and the firmware read use the same reply. If these two ever disagree, a port would
        // either be detected as a W2 whose firmware is blank, or rejected despite reporting one.
        foreach (var reply in new[] { "V1.03;", "v2.10;", "V0.01;" })
        {
            Assert.True(W2Probe.LooksLikeW2(reply));
            Assert.NotNull(W2FrameParser.Firmware(reply));
        }

        foreach (var reply in new[] { "I2314001;", "S108;", "V;" })
        {
            Assert.False(W2Probe.LooksLikeW2(reply));
            Assert.Null(W2FrameParser.Firmware(reply));
        }
    }
}
