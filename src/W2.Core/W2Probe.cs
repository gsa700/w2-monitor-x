using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

namespace W2.Core;

/// <summary>
/// Detects W2 meters by probing serial ports: open at 9600 8N1 (DTR/RTS asserted), send 'V',
/// and see if the reply looks like a firmware version. WARNING: asserting DTR/RTS and writing
/// to an arbitrary port can momentarily key a radio or cycle other gear on a CAT/PTT port — the
/// caller must confirm with the user first (matches the PowerShell app's Detect gate).
/// </summary>
public static class W2Probe
{
    private static readonly Regex Firmware = new(@"^[Vv]\d", RegexOptions.Compiled);

    /// <summary>True if a 'V' reply looks like a W2 firmware string (e.g. "V1.03;").</summary>
    public static bool LooksLikeW2(string? vReply) =>
        !string.IsNullOrEmpty(vReply) && Firmware.IsMatch(vReply.TrimStart());

    /// <summary>
    /// Probe the given ports (skipping any in <paramref name="skip"/>, e.g. already-connected
    /// ones) and return those that answered like a W2. Blocking — run on a background thread.
    /// </summary>
    public static List<string> Detect(IEnumerable<string> ports, ISet<string>? skip = null)
    {
        var found = new List<string>();
        foreach (var port in ports)
        {
            if (skip is not null && skip.Contains(port)) continue;
            if (ProbeOne(port)) found.Add(port);
        }
        return found;
    }

    private static bool ProbeOne(string port)
    {
        try
        {
            using var sp = new SerialPort(port, 9600, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                DtrEnable = true,
                RtsEnable = true,
                ReadTimeout = 250,
                WriteTimeout = 250,
                Encoding = Encoding.ASCII,
            };
            sp.Open();
            Thread.Sleep(120);
            sp.DiscardInBuffer();
            sp.Write("V");

            var resp = new StringBuilder();
            var deadline = DateTime.UtcNow.AddMilliseconds(300);
            while (DateTime.UtcNow < deadline)
            {
                while (sp.BytesToRead > 0)
                {
                    var ch = (char)sp.ReadByte();
                    resp.Append(ch);
                    if (ch == ';') return LooksLikeW2(resp.ToString());
                }
                Thread.Sleep(5);
            }
            return LooksLikeW2(resp.ToString());
        }
        catch
        {
            return false;   // in use, no permission, not a serial device, etc.
        }
    }
}
