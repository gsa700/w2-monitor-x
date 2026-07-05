namespace W2.Core;

/// <summary>Which of the W2's two samplers it is currently reading.</summary>
public enum Sampler { Unknown, S1, S2 }

/// <summary>
/// One assembled sample from an Elecraft W2. The W2 is query/response: the reader polls
/// it with single-char commands (F=forward, R=reflected, S=SWR, I=info/status) and this
/// record bundles one cycle's replies. Power/SWR are nullable: a field is null when that
/// reply was absent or unparseable this cycle (a serial dropout), so the UI layer can hold
/// last-good instead of blanking — mirroring the PowerShell app's $null handling.
///
/// Protocol confirmed against the validated PowerShell decoder (W2App.ps1):
///   F/R  [FfRr](\d+)D(\d)  -> digits / 10^places
///   S    [Ss](\d+)         -> digits / 100
///   I    'I' + payload byte map (see W2FrameParser.Info)
/// </summary>
public sealed record W2Reading
{
    public double? ForwardPowerW { get; init; }
    public double? ReflectedPowerW { get; init; }
    public double? Swr { get; init; }

    // Decoded from the I-string. HasStatus is false when the reply wasn't a valid I frame.
    public bool HasStatus { get; init; }
    public bool Alarm { get; init; }
    public Sampler ActiveSampler { get; init; } = Sampler.Unknown;
    public string? RangeName { get; init; }
    public double FullScaleW { get; init; } = 200.0;
    public string? TypeName { get; init; }
    public bool AutoRange { get; init; }
    public bool LedsOn { get; init; }

    /// <summary>Raw, non-printable-stripped I reply (kept for diagnostics).</summary>
    public string RawInfo { get; init; } = string.Empty;

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Return loss in dB, derived from SWR. Null if SWR is unknown; 60 dB cap for a match.</summary>
    public double? ReturnLossDb
    {
        get
        {
            if (Swr is not { } swr) return null;
            if (swr <= 1.0) return 60.0;
            var rl = -20.0 * Math.Log10((swr - 1.0) / (swr + 1.0));
            return double.IsFinite(rl) ? Math.Min(rl, 60.0) : 60.0;
        }
    }

    /// <summary>True when RF is present (forward power above a small floor).</summary>
    public bool IsTransmitting => ForwardPowerW is > 0.5;

    public override string ToString() =>
        $"{ForwardPowerW?.ToString("0.0") ?? "—"}W  refl {ReflectedPowerW?.ToString("0.0") ?? "—"}W  SWR {Swr?.ToString("0.00") ?? "—"}";
}
