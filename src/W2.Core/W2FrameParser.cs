using System.Globalization;
using System.Text.RegularExpressions;

namespace W2.Core;

/// <summary>
/// Decodes the W2's single-char query replies into a <see cref="W2Reading"/>.
///
/// Every format here is transcribed from the on-air-validated PowerShell decoder
/// (W2App.ps1) rather than guessed:
///   Get-Power (line 81):  ^[FfRr](\d+)D(\d);  ->  digits / 10^places   (F and R share this)
///   Get-Swr   (line 82):  ^[Ss](\d+);         ->  digits / 100
///   I-string  (lines 503-522): strip non-printable; '^[Aa]!' = alarm; else 'I' + payload,
///     indexing the payload AFTER the leading 'I' (b = info.TrimEnd(';').Substring(1)):
///       b[1] range   1=2W 2=20W 3=200W 4=2kW  (full-scale 2/20/200/2000 W)
///       b[2] auto-range ('1' = on)
///       b[3] type    0=HF 200W 1=HF 2 kW 2=VHF/UHF
///       b[5] LEDs ('1' = on)
///       b[6] active sampler 1=S1 2=S2  (0=none while hunting in Search mode)
///     Per the authoritative manual (W2 Serial Interface Commands Rev D), the two bytes
///     the PowerShell app doesn't surface are b[0]=active sensor matching the lit S1/S2 LED
///     and b[4]=internal attenuator (0/1) — decode them here if a UI ever needs them.
///
/// The RF-gated "hold sensor/type/range while idle" anti-strobe is display logic and lives
/// in the UI layer (Phase 2), not here — this decodes whatever the frame actually reports.
/// </summary>
public static class W2FrameParser
{
    private static readonly Regex PowerRx = new(@"^[FfRr](\d+)D(\d)", RegexOptions.Compiled);
    private static readonly Regex SwrRx = new(@"^[Ss](\d+)", RegexOptions.Compiled);
    private static readonly Regex TripRx = new(@"[\[\]](\d+)", RegexOptions.Compiled);

    private static readonly Dictionary<char, (string Name, double FullScale)> Range = new()
    {
        ['1'] = ("2 W", 2.0),
        ['2'] = ("20 W", 20.0),
        ['3'] = ("200 W", 200.0),
        ['4'] = ("2 kW", 2000.0),
    };

    private static readonly Dictionary<char, string> Type = new()
    {
        ['0'] = "HF 200W",
        ['1'] = "HF 2 kW",
        ['2'] = "VHF/UHF",
    };

    /// <summary>Forward or reflected power: digits / 10^places, e.g. "F1500D1" -> 150.0. Null if unmatched.</summary>
    public static double? Power(string? reply)
    {
        if (string.IsNullOrEmpty(reply)) return null;
        var m = PowerRx.Match(reply);
        if (!m.Success) return null;
        if (!long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var digits))
            return null;
        return digits / Math.Pow(10, m.Groups[2].Value[0] - '0');
    }

    /// <summary>SWR: digits / 100, e.g. "S150" -> 1.50. Null if unmatched.</summary>
    public static double? Swr(string? reply)
    {
        if (string.IsNullOrEmpty(reply)) return null;
        var m = SwrRx.Match(reply);
        if (!m.Success) return null;
        if (!long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var digits))
            return null;
        return digits / 100.0;
    }

    /// <summary>SWR-alarm trip point echo: "[nn;" / "]nn;" → nn/10 (1.1–5.0). Null if unmatched.</summary>
    public static double? AlarmTrip(string? reply)
    {
        if (string.IsNullOrEmpty(reply)) return null;
        var m = TripRx.Match(reply);
        if (!m.Success) return null;
        return long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n / 10.0 : null;
    }

    /// <summary>Decode the I (info/status) reply. See the class remarks for the byte map.</summary>
    public static W2Info Info(string? reply)
    {
        if (string.IsNullOrEmpty(reply)) return new W2Info();

        // Keep only printable ASCII, matching the PowerShell app's `-replace '[^\x20-\x7E]',''`.
        var info = new string(reply.Where(c => c is >= (char)0x20 and <= (char)0x7E).ToArray());

        if (Regex.IsMatch(info, "^[Aa]!")) return new W2Info { Alarm = true };
        if (info.Length < 2 || (info[0] != 'I' && info[0] != 'i')) return new W2Info();

        var b = info.TrimEnd(';')[1..];   // drop the leading 'I'

        var autoRange = b.Length >= 3 && b[2] == '1';
        var leds = b.Length >= 6 && b[5] == '1';

        var sampler = Sampler.Unknown;
        if (b.Length >= 7)
            sampler = b[6] switch { '1' => Sampler.S1, '2' => Sampler.S2, _ => Sampler.Unknown };

        string? rangeName = null;
        var fullScale = 200.0;
        if (b.Length >= 2 && Range.TryGetValue(b[1], out var rg)) { rangeName = rg.Name; fullScale = rg.FullScale; }

        string? typeName = null;
        if (b.Length >= 4 && Type.TryGetValue(b[3], out var tp)) typeName = tp;

        return new W2Info
        {
            Valid = true,
            ActiveSampler = sampler,
            RangeName = rangeName,
            FullScaleW = fullScale,
            TypeName = typeName,
            AutoRange = autoRange,
            LedsOn = leds,
        };
    }

    /// <summary>Assemble one reading from a poll cycle's replies.</summary>
    public static W2Reading Build(string? f, string? r, string? s, string? info)
    {
        var status = Info(info);
        return new W2Reading
        {
            ForwardPowerW = Power(f),
            ReflectedPowerW = Power(r),
            Swr = Swr(s),
            HasStatus = status.Valid,
            Alarm = status.Alarm,
            ActiveSampler = status.ActiveSampler,
            RangeName = status.RangeName,
            FullScaleW = status.FullScaleW,
            TypeName = status.TypeName,
            AutoRange = status.AutoRange,
            LedsOn = status.LedsOn,
            RawInfo = info ?? string.Empty,
            TimestampUtc = DateTime.UtcNow,
        };
    }
}

/// <summary>Decoded I (info/status) reply. <see cref="Valid"/> is false for a non-I frame.</summary>
public sealed record W2Info
{
    public bool Valid { get; init; }
    public bool Alarm { get; init; }
    public Sampler ActiveSampler { get; init; } = Sampler.Unknown;
    public string? RangeName { get; init; }
    public double FullScaleW { get; init; } = 200.0;
    public string? TypeName { get; init; }
    public bool AutoRange { get; init; }
    public bool LedsOn { get; init; }
}
