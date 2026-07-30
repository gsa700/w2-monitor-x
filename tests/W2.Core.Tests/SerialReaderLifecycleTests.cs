using W2.Core;
using Xunit;

namespace W2.Core.Tests;

/// <summary>
/// Start/Stop/Dispose lifecycle only — no port is ever opened, so these run anywhere. The reader's
/// actual serial behavior still needs hardware (see HANDOFF-PI.md's harness); these pin the shutdown
/// contract, which is pure object lifecycle.
///
/// Worth knowing what was and wasn't broken here: repeat Dispose/Stop never actually threw, because
/// <c>ManualResetEventSlim.Dispose()</c> is itself idempotent and its <c>Set()</c> tolerates being
/// called after disposal. These tests hold that line rather than fixing a live crash. The genuinely
/// dangerous post-disposal call was <c>Wait()</c>, which does throw — on the supervisor thread, where
/// an escaping exception takes the process down. That one can't be reached without a port, so it's
/// covered by construction in the reader (WaitForStop plus a catch-all) rather than by a test here.
/// </summary>
public class SerialReaderLifecycleTests
{
    [Fact]
    public void Dispose_is_idempotent()
    {
        var r = new SerialReader();
        r.Dispose();
        r.Dispose();
    }

    [Fact]
    public void Stop_without_start_is_safe()
    {
        var r = new SerialReader();
        r.Stop();
        r.Stop();
    }

    [Fact]
    public void Stop_after_dispose_is_safe()
    {
        var r = new SerialReader();
        r.Dispose();
        r.Stop();   // a shutdown path must never throw at a caller who is only tidying up
    }

    [Fact]
    public void Start_after_dispose_throws_objectdisposed()
    {
        // Threw before this batch too, but incidentally — from Reset() deep inside Start, after Stop()
        // had already run. Now it's an explicit guard on the first line, so the reason is legible.
        var r = new SerialReader();
        r.Dispose();
        Assert.Throws<ObjectDisposedException>(() => r.Start("COM_NOT_A_REAL_PORT"));
    }

    [Fact]
    public void Send_before_start_is_ignored()
    {
        var r = new SerialReader();
        r.Send('N');   // queued against no session; must not throw
        r.Dispose();
    }

    [Fact]
    public void Is_not_running_before_start_or_after_dispose()
    {
        var r = new SerialReader();
        Assert.False(r.IsRunning);
        r.Dispose();
        Assert.False(r.IsRunning);
    }
}
