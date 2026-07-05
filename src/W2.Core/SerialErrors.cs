namespace W2.Core;

/// <summary>
/// Turns raw serial exceptions into actionable, platform-aware messages. The big one on
/// Linux/Pi is permission denied on /dev/tty* when the user isn't in the 'dialout' group;
/// on Windows the same exception usually means another app already holds the port.
/// </summary>
public static class SerialErrors
{
    public static string Describe(Exception ex, string port, bool isLinux)
    {
        return ex switch
        {
            UnauthorizedAccessException when isLinux =>
                $"Permission denied on {port}. Add your user to the 'dialout' group: " +
                "sudo usermod -aG dialout $USER  (then log out and back in).",
            UnauthorizedAccessException =>
                $"{port} is in use or access denied — another app may have it open.",
            FileNotFoundException =>
                $"{port} not found — is the W2 plugged in?",
            System.IO.IOException =>
                $"{port} could not be opened — check the cable and that the W2 is powered.",
            _ => $"Error: {ex.Message}",
        };
    }
}
