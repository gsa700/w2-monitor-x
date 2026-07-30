namespace W2.Core;

/// <summary>
/// Formats a cable identity for compact display. On Windows the stored serial is already short
/// (the FTDI chip serial, e.g. "A10KMB4VA"). On Linux it's the long /dev/serial/by-id name
/// (e.g. "usb-FTDI_FT230X_Basic_UART_A10KMB4VA-if00-port0"); we pull the embedded serial out of
/// that and prefix a "…" to signal it was shortened, keeping it about the Windows length.
///
/// The two ellipses mean different things and are decided independently: a <b>leading</b> "…" means
/// the serial was extracted out of a longer by-id name, and a <b>trailing</b> one means the result was
/// still too long and got truncated. They used to share one condition ("shorter than the input"), so a
/// plain over-length raw serial came back as "…VERYLONGS…" — falsely claiming a by-id extraction that
/// never happened, on a string that had merely been cut short.
/// </summary>
public static class SerialDisplay
{
    private const int MaxLen = 10;

    public static string? Shorten(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial)) return null;
        var s = serial.Trim();
        var extracted = s;

        // /dev/serial/by-id form: usb-<mfr>_<product>_<SERIAL>-ifNN-portN → take the serial token.
        if (s.StartsWith("usb-", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("-if", StringComparison.OrdinalIgnoreCase))
        {
            var ifIdx = s.IndexOf("-if", StringComparison.OrdinalIgnoreCase);
            var core = ifIdx > 0 ? s[..ifIdx] : s;
            var tok = core.Split('_', '-').LastOrDefault(t => t.Length > 0);
            if (!string.IsNullOrEmpty(tok)) extracted = tok!;
        }

        // Did we actually pull a serial out of a longer identifier? That, and only that, earns the
        // leading "…" — a truncation below is marked by its own trailing one.
        var pulledFromByIdName = extracted.Length < s.Length;

        if (extracted.Length > MaxLen) extracted = extracted[..(MaxLen - 1)] + "…";

        return pulledFromByIdName ? "…" + extracted : extracted;
    }
}
