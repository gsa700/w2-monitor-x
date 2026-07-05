using System.Globalization;

namespace W2.Core;

/// <summary>
/// Encodes values back into W2 wire replies — the inverse of <see cref="W2FrameParser"/>.
/// Used by <see cref="W2SimReader"/> so the simulator feeds the real decode pipeline, and
/// it doubles as living documentation of the wire format (round-trip tested against the parser).
/// </summary>
public static class W2Wire
{
    /// <summary>Forward/reflected power: "{F|R}{digits}D{places};" where value = digits / 10^places.</summary>
    public static string EncodePower(char cmd, double watts, int places = 1)
    {
        if (watts < 0) watts = 0;
        var digits = (long)Math.Round(watts * Math.Pow(10, places));
        return $"{cmd}{digits.ToString(CultureInfo.InvariantCulture)}D{places};";
    }

    /// <summary>SWR: "S{digits};" where value = digits / 100.</summary>
    public static string EncodeSwr(double swr)
    {
        if (swr < 1.0) swr = 1.0;
        var digits = (long)Math.Round(swr * 100);
        return $"S{digits.ToString(CultureInfo.InvariantCulture)};";
    }

    /// <summary>
    /// Info/status: 'I' + 7-byte payload. See <see cref="W2FrameParser.Info"/> for the map:
    /// [0]=LED-matched sensor [1]=range [2]=auto [3]=type [4]=attenuator [5]=LEDs [6]=active sampler.
    /// </summary>
    public static string EncodeInfo(Sampler active, char rangeKey, bool autoRange, char typeKey,
        bool leds, bool attenuator = false)
    {
        var a = active switch { Sampler.S1 => '1', Sampler.S2 => '2', _ => '0' };
        var payload = new[]
        {
            a,                          // [0] LED-matched sensor
            rangeKey,                   // [1] range
            autoRange ? '1' : '0',      // [2] auto-range
            typeKey,                    // [3] type
            attenuator ? '1' : '0',     // [4] internal attenuator
            leds ? '1' : '0',           // [5] LEDs
            a,                          // [6] active sampler
        };
        return $"I{new string(payload)};";
    }
}
