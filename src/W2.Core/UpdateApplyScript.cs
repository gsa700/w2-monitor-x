namespace W2.Core;

/// <summary>
/// Builds the detached helper script the updater launches: it waits for the running app to exit,
/// copies the freshly staged executable over the installed one, and relaunches. A running exe can't
/// overwrite itself, hence the wait-then-swap helper.
///
/// The important correctness detail is the copy check. Both <c>Copy-Item</c> (PowerShell) and <c>cp</c>
/// fail non-fatally, so the original scripts relaunched unconditionally — a failed copy (file locked,
/// no write permission) would relaunch the OLD exe while the UI had already said "restarting to
/// apply…", i.e. a silent false success. Here the copy result is checked: on success the new build
/// relaunches; on failure a marker file is dropped (the app surfaces it on next start) and the old
/// exe is relaunched anyway so the user is never left without a running app.
/// </summary>
public static class UpdateApplyScript
{
    public static string Windows(int pid, string stagedExe, string targetExe, string failedMarker) =>
        $"while (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 300 }}\n" +
        $"Copy-Item -LiteralPath '{stagedExe}' -Destination '{targetExe}' -Force\n" +
        // Copy-Item is non-terminating; $? reflects whether it actually succeeded.
        $"if ($?) {{ Remove-Item -LiteralPath '{failedMarker}' -ErrorAction SilentlyContinue }}\n" +
        $"else {{ New-Item -ItemType File -Path '{failedMarker}' -Force | Out-Null }}\n" +
        $"Start-Process -FilePath '{targetExe}'\n";

    public static string Unix(int pid, string stagedExe, string targetExe, string failedMarker) =>
        "#!/bin/sh\n" +
        $"while kill -0 {pid} 2>/dev/null; do sleep 0.3; done\n" +
        // Relaunch of the new build is gated on cp succeeding; the else branch records the failure.
        $"if cp -f '{stagedExe}' '{targetExe}'; then\n" +
        $"  chmod +x '{targetExe}'\n" +
        $"  rm -f '{failedMarker}'\n" +
        "else\n" +
        $"  : > '{failedMarker}'\n" +
        "fi\n" +
        $"'{targetExe}' &\n";
}
