namespace W2.Core;

/// <summary>
/// The decision that keeps a meter's saved pin honest. Pure, so it is tested — the same class of
/// bug shipped in LP-100A's 1.0.0 precisely because the rule lived inline in the close handler,
/// where nothing exercised the "meter absent" case.
///
/// The pin is the adapter's chip serial (Windows) or its <c>/dev/serial/by-id</c> name (Linux), plus
/// the last port it was seen on. It exists so the meter is found again after a renumber. It must
/// therefore only ever describe the meter — and the meter is only known through a connection.
///
/// This differs from LP-100A's rule in one deliberate way: the identity is captured when the port is
/// <em>opened</em>, not looked up when the config is <em>saved</em>. A save-time lookup still has the
/// hole it was meant to close — unplug the meter, let another USB serial inherit its <c>ttyUSB</c>
/// name, close the app, and the lookup returns the other device. The box these meters live on also
/// carries a Victron cable, so that is not hypothetical.
/// </summary>
public static class PortPin
{
    /// <summary>What to save for one meter on exit.</summary>
    /// <param name="openedPort">The port this meter's reader most recently opened this run, or null if it never did.</param>
    /// <param name="openedSerial">The adapter identity read at that open, or null if none was available.</param>
    /// <param name="assignedPort">The meter's current port — resolved by cable, or picked in Setup.</param>
    /// <param name="savedSerial">The identity loaded at startup, or learned earlier this run.</param>
    /// <returns>
    /// The port is always <paramref name="assignedPort"/>: a pick made in Setup is the user's call
    /// even before they connect, and a port the cable moved to is where the meter is. The serial is
    /// refreshed only when the assignment is the very port that was opened — otherwise the saved
    /// identity is kept, because nothing this run has proved what is on the assigned port.
    /// </returns>
    public static (string? Port, string? Serial) ForSave(
        string? openedPort, string? openedSerial, string? assignedPort, string? savedSerial)
    {
        if (assignedPort is null) return (null, savedSerial);
        if (openedPort is not null && string.Equals(openedPort, assignedPort, StringComparison.OrdinalIgnoreCase))
            return (assignedPort, openedSerial ?? savedSerial);
        return (assignedPort, savedSerial);
    }
}
