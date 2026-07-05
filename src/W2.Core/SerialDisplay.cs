namespace W2.Core;

/// <summary>
/// Formats a cable identity for compact display. On Windows the stored serial is already short
/// (the FTDI chip serial, e.g. "A10KMB4VA"). On Linux it's the long /dev/serial/by-id name
/// (e.g. "usb-FTDI_FT230X_Basic_UART_A10KMB4VA-if00-port0"); we pull the embedded serial out of
/// that and prefix a "…" to signal it was shortened, keeping it about the Windows length.
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

        if (extracted.Length > MaxLen) extracted = extracted[..(MaxLen - 1)] + "…";

        // Leading … marks that we shortened a longer identifier (Linux); short serials pass through.
        return extracted.Length < s.Length ? "…" + extracted : extracted;
    }
}
