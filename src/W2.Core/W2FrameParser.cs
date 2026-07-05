using System.Globalization;
using System.Text.RegularExpressions;

namespace W2.Core;

/// <summary>
/// Decodes the W2's single-char query replies into a <see cref="W2Reading"/>.
///
/// The forward-power reply format is confirmed from the PowerShell app
/// (W2App.ps1:144):  [Ff](\d+)D(\d)  ->  value = digits / 10^D.
/// Reflected (R) and SWR (S) are wired through the SAME shape here on the
/// assumption they mirror F; this is UNVERIFIED — Phase 1 must confirm both
/// against real captures (reuse the LP-100A Capture-*.ps1 approach on a W2).
///
/// The I-string is not decoded yet; it is passed through as RawInfo. Its byte
/// map (sensor/range/type/alarm/full-scale) is the Phase 1 headline task.
/// </summary>
public static class W2FrameParser
{
    private static readonly Regex Fwd = new(@"[Ff](\d+)D(\d)", RegexOptions.Compiled);
    private static readonly Regex Rfl = new(@"[Rr](\d+)D(\d)", RegexOptions.Compiled);   // ASSUMED — verify Phase 1
    private static readonly Regex Swr = new(@"[Ss](\d+)D(\d)", RegexOptions.Compiled);   // ASSUMED — verify Phase 1

    /// <summary>digits / 10^places, e.g. "F1500D1" -> 150.0. Null if it doesn't match.</summary>
    private static double? Decode(Regex rx, string? reply)
    {
        if (string.IsNullOrEmpty(reply)) return null;
        var m = rx.Match(reply);
        if (!m.Success) return null;
        if (!long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var digits))
            return null;
        var places = m.Groups[2].Value[0] - '0';
        return digits / Math.Pow(10, places);
    }

    public static double? Forward(string? reply) => Decode(Fwd, reply);
    public static double? Reflected(string? reply) => Decode(Rfl, reply);
    public static double? StandingWaveRatio(string? reply) => Decode(Swr, reply);

    /// <summary>Assemble one reading from a poll cycle's replies. Missing fields read 0.</summary>
    public static W2Reading Build(string? f, string? r, string? s, string? info) => new()
    {
        ForwardPowerW = Forward(f) ?? 0.0,
        ReflectedPowerW = Reflected(r) ?? 0.0,
        Swr = StandingWaveRatio(s) ?? 0.0,
        RawInfo = info ?? string.Empty,
        TimestampUtc = DateTime.UtcNow,
    };
}
