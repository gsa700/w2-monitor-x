using W2.Core;
using Xunit;

namespace W2.Core.Tests;

public class SerialDisplayTests
{
    [Fact]
    public void Null_or_empty_returns_null()
    {
        Assert.Null(SerialDisplay.Shorten(null));
        Assert.Null(SerialDisplay.Shorten("   "));
    }

    [Theory]
    [InlineData("A10KMB4VA")]   // Windows FTDI serial — already short, unchanged
    [InlineData("AG0JFX7UA")]
    public void Windows_serial_passes_through(string s) =>
        Assert.Equal(s, SerialDisplay.Shorten(s));

    [Fact]
    public void Linux_byid_extracts_serial_with_leading_ellipsis()
    {
        var s = SerialDisplay.Shorten("usb-FTDI_FT230X_Basic_UART_A10KMB4VA-if00-port0");
        Assert.Equal("…A10KMB4VA", s);
    }

    [Fact]
    public void Linux_byid_stays_about_windows_length()
    {
        var s = SerialDisplay.Shorten("usb-FTDI_FT230X_Basic_UART_A10KMB4VA-if00-port0")!;
        Assert.True(s.Length <= 12, $"too long: '{s}' ({s.Length})");
    }

    [Fact]
    public void Overlong_token_is_capped_with_ellipsis()
    {
        var s = SerialDisplay.Shorten("VERYLONGSERIALNUMBER1234")!;
        Assert.EndsWith("…", s);
        Assert.True(s.Length <= 11);
    }

    [Fact]
    public void Truncating_a_raw_serial_does_not_claim_a_byid_extraction()
    {
        // The leading "…" means "pulled out of a long by-id name". A plain over-length serial was
        // merely cut short, so it gets the trailing mark only — not both.
        var s = SerialDisplay.Shorten("VERYLONGSERIALNUMBER1234")!;
        Assert.DoesNotContain("…", s[..^1]);
        Assert.Equal("VERYLONGS…", s);
    }

    [Fact]
    public void A_byid_name_with_an_overlong_serial_gets_both_marks()
    {
        // Both things genuinely happened here: extracted from the by-id name, then still too long.
        var s = SerialDisplay.Shorten("usb-FTDI_FT230X_Basic_UART_ABCDEFGHIJKLMNOP-if00-port0")!;
        Assert.StartsWith("…", s);
        Assert.EndsWith("…", s);
        Assert.Equal("…ABCDEFGHI…", s);
    }
}
