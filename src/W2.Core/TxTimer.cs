namespace W2.Core;

/// <summary>TX-timeout alert level for the timer readout.</summary>
public enum TxAlert { Idle, Normal, Warning, Over }

/// <summary>
/// Pure TX timeout-timer rule (extracted for testing). Matches the PowerShell app: solid
/// warning for the last 30 s before the timeout, then "over" (flashing) at/after it while the
/// timer keeps counting. Silent — nothing here goes over the air.
/// </summary>
public static class TxTimer
{
    public const int WarnSeconds = 30;

    public static TxAlert Evaluate(bool transmitting, double elapsedSeconds, int timeoutSec)
    {
        if (!transmitting) return TxAlert.Idle;
        if (timeoutSec > 0 && elapsedSeconds >= timeoutSec) return TxAlert.Over;
        if (timeoutSec > 0 && elapsedSeconds >= timeoutSec - WarnSeconds) return TxAlert.Warning;
        return TxAlert.Normal;
    }

    public static string Format(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }
}
