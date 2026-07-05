namespace W2.Core;

/// <summary>
/// One assembled sample from an Elecraft W2. Unlike the LP-100A (one comma frame per
/// poll), the W2 is query/response: the reader polls it with single-char commands
/// (F=forward, R=reflected, S=SWR, I=info/status) and this record bundles one cycle's
/// worth of replies.
///
/// PHASE 1 TODO: the I-string is a byte map (sensor/active sampler, range, type, alarm,
/// full-scale). It is carried here as <see cref="RawInfo"/> until the byte-map decoder
/// lands and is validated against real captures. See w2-serial-protocol-ref.
/// </summary>
public sealed record W2Reading
{
    public double ForwardPowerW { get; init; }
    public double ReflectedPowerW { get; init; }
    public double Swr { get; init; }

    /// <summary>Raw, undecoded I-string reply (Phase 1 will parse this).</summary>
    public string RawInfo { get; init; } = string.Empty;

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Return loss in dB, derived from SWR. Capped at 60 dB for a perfect match.</summary>
    public double ReturnLossDb
    {
        get
        {
            if (Swr <= 1.0) return 60.0;
            var rl = -20.0 * Math.Log10((Swr - 1.0) / (Swr + 1.0));
            return double.IsFinite(rl) ? Math.Min(rl, 60.0) : 60.0;
        }
    }

    /// <summary>True when RF is present (forward power above a small floor).</summary>
    public bool IsTransmitting => ForwardPowerW > 0.5;

    public override string ToString() =>
        $"{ForwardPowerW:0.0}W  refl {ReflectedPowerW:0.0}W  SWR {Swr:0.00}";
}
