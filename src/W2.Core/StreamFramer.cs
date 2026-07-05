using System.Text;

namespace W2.Core;

/// <summary>
/// Reads one ';'-terminated W2 reply from a serial stream. The W2 answers each
/// single-char query with a short field ending in ';' (see the PowerShell app's
/// ReadUntil/Query helpers). This accumulates bytes until the terminator, then
/// hands back the body with the ';' stripped.
///
/// Kept as a small standalone piece (mirrors the LP-100A framer) so the reader
/// loop and unit tests can share the exact same framing logic.
/// </summary>
public sealed class ReplyFramer
{
    private readonly StringBuilder _acc = new();

    /// <summary>Feed decoded text; returns each complete reply body (';' stripped).</summary>
    public List<string> Feed(string chunk)
    {
        var replies = new List<string>();
        _acc.Append(chunk);

        var s = _acc.ToString();
        var parts = s.Split(';');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var body = parts[i].Trim();
            if (body.Length > 0) replies.Add(body);
        }

        _acc.Clear();
        _acc.Append(parts[^1]);
        return replies;
    }

    public void Reset() => _acc.Clear();
}
