using W2.Core;
using Xunit;

namespace W2.Core.Tests;

public class PortPinTests
{
    private const string Meter = "usb-FTDI_FT232R_USB_UART_A10KMB4V-if00-port0";
    private const string Shunt = "usb-VictronEnergy_BV_VE_Direct_cable_VEAUI3T2-if00-port0";

    [Fact]
    public void NoConnectionThisRunKeepsTheSavedIdentity()
    {
        // Never connected: the assignment is kept (the user may have just picked it), the identity
        // is not touched, whatever device happens to be on that port now.
        var (port, serial) = PortPin.ForSave(null, null, "/dev/ttyUSB2", Meter);
        Assert.Equal("/dev/ttyUSB2", port);
        Assert.Equal(Meter, serial);
    }

    [Fact]
    public void AConnectionRefreshesTheIdentityFromWhatWasOpened()
    {
        var (port, serial) = PortPin.ForSave("/dev/ttyUSB5", Meter, "/dev/ttyUSB5", "stale-serial");
        Assert.Equal("/dev/ttyUSB5", port);
        Assert.Equal(Meter, serial);
    }

    [Fact]
    public void ANameReusedByAnotherDeviceAfterUnplugDoesNotOverwriteTheMeter()
    {
        // The exposure this rule exists for: opened ttyUSB2 as the meter, meter unplugged, the Victron
        // cable inherited ttyUSB2, app closed. The identity captured at open time is what gets saved;
        // the live map is never consulted, so the shunt cannot become "the meter".
        var (port, serial) = PortPin.ForSave("/dev/ttyUSB2", Meter, "/dev/ttyUSB2", Meter);
        Assert.Equal("/dev/ttyUSB2", port);
        Assert.Equal(Meter, serial);
        Assert.NotEqual(Shunt, serial);
    }

    [Fact]
    public void ReassignedInSetupAfterConnectingKeepsThePickAndTheOldIdentity()
    {
        // Connected on USB2, then the user picked USB0 in Setup without reconnecting. The pick is
        // honored; the identity is not refreshed from a port that was never opened as this meter.
        var (port, serial) = PortPin.ForSave("/dev/ttyUSB2", Meter, "/dev/ttyUSB0", Meter);
        Assert.Equal("/dev/ttyUSB0", port);
        Assert.Equal(Meter, serial);
    }

    [Fact]
    public void NoIdentityAvailableAtOpenKeepsTheSavedOne()
    {
        // No /dev/serial/by-id on a minimal image: the open succeeded but nothing to identify it by.
        var (port, serial) = PortPin.ForSave("COM4", null, "COM4", "A10KMB4VA");
        Assert.Equal("COM4", port);
        Assert.Equal("A10KMB4VA", serial);
    }

    [Fact]
    public void FirstEverConnectionLearnsTheIdentity()
    {
        var (port, serial) = PortPin.ForSave("COM4", "A10KMB4VA", "COM4", null);
        Assert.Equal("COM4", port);
        Assert.Equal("A10KMB4VA", serial);
    }

    [Fact]
    public void NoAssignmentSavesNoPort()
    {
        var (port, serial) = PortPin.ForSave("COM4", "A10KMB4VA", null, "kept");
        Assert.Null(port);
        Assert.Equal("kept", serial);
    }

    [Fact]
    public void WindowsPortNamesCompareCaseInsensitively()
    {
        var (port, serial) = PortPin.ForSave("com4", "A10KMB4VA", "COM4", null);
        Assert.Equal("COM4", port);
        Assert.Equal("A10KMB4VA", serial);
    }
}
