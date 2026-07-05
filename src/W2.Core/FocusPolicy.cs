namespace W2.Core;

/// <summary>Just the facts the focus rule needs about one meter.</summary>
public readonly record struct MeterFocusState(string Id, bool IsConnected, bool IsTransmitting, double OverPeakW);

/// <summary>
/// Pure auto-focus rule, extracted so it can be unit-tested without the UI/Dispatcher. Mirrors
/// the PowerShell app: a transmitting meter wins (highest over-peak if several key at once);
/// otherwise the manual pick (if still connected); otherwise the first connected meter.
/// </summary>
public static class FocusPolicy
{
    public static string? Pick(IReadOnlyList<MeterFocusState> meters, string? manualId)
    {
        MeterFocusState? tx = null;
        string? firstConnected = null;
        var manualConnected = false;

        foreach (var m in meters)
        {
            if (!m.IsConnected) continue;
            firstConnected ??= m.Id;
            if (m.Id == manualId) manualConnected = true;
            if (m.IsTransmitting && (tx is null || m.OverPeakW > tx.Value.OverPeakW)) tx = m;
        }

        if (tx is not null) return tx.Value.Id;
        if (manualId is not null && manualConnected) return manualId;
        return firstConnected;
    }
}
