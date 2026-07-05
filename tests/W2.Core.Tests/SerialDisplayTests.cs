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
}
