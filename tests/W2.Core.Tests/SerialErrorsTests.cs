using W2.Core;
using Xunit;

namespace W2.Core.Tests;

public class SerialErrorsTests
{
    [Fact]
    public void Linux_permission_denied_mentions_dialout()
    {
        var msg = SerialErrors.Describe(new UnauthorizedAccessException(), "/dev/ttyUSB0", isLinux: true);
        Assert.Contains("dialout", msg);
        Assert.Contains("/dev/ttyUSB0", msg);
    }

    [Fact]
    public void Windows_access_denied_mentions_another_app()
    {
        var msg = SerialErrors.Describe(new UnauthorizedAccessException(), "COM8", isLinux: false);
        Assert.Contains("COM8", msg);
        Assert.Contains("another app", msg);
        Assert.DoesNotContain("dialout", msg);
    }

    [Fact]
    public void Missing_port_asks_if_plugged_in()
    {
        var msg = SerialErrors.Describe(new FileNotFoundException(), "COM8", isLinux: false);
        Assert.Contains("plugged in", msg);
    }

    [Fact]
    public void Unknown_error_falls_back_to_message()
    {
        var msg = SerialErrors.Describe(new InvalidOperationException("boom"), "COM8", isLinux: false);
        Assert.Contains("boom", msg);
    }
}
