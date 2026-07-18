using W2.Core;
using Xunit;

namespace W2.Core.Tests;

public class UpdateApplyScriptTests
{
    const int Pid = 4242;
    const string Staged = "/tmp/W2Monitor-update/ex/W2Monitor";
    const string Target = "/opt/w2monitor/W2Monitor";
    const string Marker = "/opt/w2monitor/.w2monitor-update-failed";

    [Fact]
    public void Windows_waits_for_the_process_then_copies_and_relaunches()
    {
        var s = UpdateApplyScript.Windows(Pid, Staged, Target, Marker);
        Assert.Contains($"-Id {Pid}", s);
        Assert.Contains(Staged, s);
        Assert.Contains(Target, s);
        Assert.Contains("Start-Process", s);
    }

    [Fact]
    public void Windows_gates_the_relaunch_impression_on_the_copy_result()
    {
        var s = UpdateApplyScript.Windows(Pid, Staged, Target, Marker);
        // Copy-Item is non-terminating, so the script must check $? and record failure via the marker.
        Assert.Contains("if ($?)", s);
        Assert.Contains(Marker, s);
        // Success path clears any stale marker; failure path creates one.
        Assert.Contains("Remove-Item", s);
        Assert.Contains("New-Item", s);
    }

    [Fact]
    public void Unix_only_relaunches_the_new_build_when_cp_succeeds()
    {
        var s = UpdateApplyScript.Unix(Pid, Staged, Target, Marker);
        Assert.Contains($"kill -0 {Pid}", s);
        // Relaunch of the swapped exe is conditional on cp succeeding.
        Assert.Contains($"if cp -f '{Staged}' '{Target}'; then", s);
        Assert.Contains("chmod +x", s);
        Assert.Contains(Marker, s);   // failure branch records the marker
        Assert.Contains($"'{Target}' &", s);
    }
}
