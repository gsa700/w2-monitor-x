using W2.Core;
using Xunit;

namespace W2.Core.Tests;

public class SensorLockTests
{
    [Fact]
    public void Single_sampler_all_accepted()
    {
        var s = new SensorLock();
        Assert.True(s.Accept(Sampler.S1, 0.0));    // idle
        Assert.True(s.Accept(Sampler.S1, 100.0));  // TX — locks
        Assert.True(s.Accept(Sampler.S1, 95.0));
        Assert.Equal(Sampler.S1, s.Locked);
    }

    [Fact]
    public void Locks_to_strong_sampler_and_rejects_stray()
    {
        var s = new SensorLock();
        Assert.True(s.Accept(Sampler.S1, 100.0));  // real over → lock S1
        Assert.False(s.Accept(Sampler.S2, 0.4));   // stray on S2 → ignored
        Assert.True(s.Accept(Sampler.S1, 98.0));   // back to the real one → shown
        Assert.False(s.Accept(Sampler.S2, 0.6));   // stray again → ignored
        Assert.Equal(Sampler.S1, s.Locked);
    }

    [Fact]
    public void Stray_first_does_not_hijack_the_real_over()
    {
        var s = new SensorLock();
        Assert.True(s.Accept(Sampler.S2, 0.7));     // weak stray arrives first → tentatively locks S2
        Assert.True(s.Accept(Sampler.S1, 100.0));   // real over is far stronger → steals the lock
        Assert.Equal(Sampler.S1, s.Locked);
        Assert.False(s.Accept(Sampler.S2, 0.7));    // stray now ignored
    }

    [Fact]
    public void Releases_when_the_over_ends()
    {
        var s = new SensorLock();
        s.Accept(Sampler.S1, 100.0);                // lock S1
        Assert.False(s.Accept(Sampler.S2, 0.5));    // stray ignored
        Assert.True(s.Accept(Sampler.S1, 0.0));     // S1 drops → over ends, release
        Assert.Equal(Sampler.Unknown, s.Locked);
        Assert.True(s.Accept(Sampler.S2, 0.5));     // now free to follow either
    }

    [Fact]
    public void Peak_hold_prevents_a_trough_from_letting_stray_steal()
    {
        var s = new SensorLock();
        s.Accept(Sampler.S1, 100.0);                // peak 100
        Assert.False(s.Accept(Sampler.S2, 5.0));    // 5 < 100*1.5 → ignored
        Assert.True(s.Accept(Sampler.S1, 3.0));     // voice trough on the real sampler (still TX)
        Assert.False(s.Accept(Sampler.S2, 5.0));    // still ignored — peak (100) holds, not the 3 W dip
        Assert.Equal(Sampler.S1, s.Locked);
    }

    [Fact]
    public void Unknown_sampler_is_always_accepted()
    {
        var s = new SensorLock();
        s.Accept(Sampler.S1, 100.0);
        Assert.True(s.Accept(Sampler.Unknown, 100.0));   // unattributable → don't reject
    }

    [Fact]
    public void Releases_after_a_long_run_of_misses()
    {
        var s = new SensorLock(releaseAfterFrames: 5);
        s.Accept(Sampler.S1, 100.0);                 // lock S1
        for (var i = 0; i < 5; i++) s.Accept(Sampler.S2, 0.4);   // idle stray only, S1 never returns
        Assert.Equal(Sampler.Unknown, s.Locked);     // bailed out of the stuck lock
    }

    [Fact]
    public void Follows_rf_when_you_key_the_other_sampler()
    {
        // Separate overs: key S1, then key S2. The W2 locks to S2 and stops visiting S1, so the
        // locked sampler goes quiet — we should switch to S2 promptly (not ignore it for seconds).
        var s = new SensorLock(switchAfterFrames: 3);
        s.Accept(Sampler.S1, 100.0);                 // lock S1
        Assert.False(s.Accept(Sampler.S2, 100.0));   // S1 quiet 1 frame — hold
        Assert.False(s.Accept(Sampler.S2, 100.0));   // 2
        Assert.True(s.Accept(Sampler.S2, 100.0));    // 3 → RF has moved: follow it to S2
        Assert.Equal(Sampler.S2, s.Locked);
    }

    [Fact]
    public void Interleaved_stray_does_not_trigger_the_move_switch()
    {
        // Original bug scenario: within one over the W2 keeps hunting back to the live S1, so the
        // locked sampler never stays quiet long enough — the stray on S2 stays ignored.
        var s = new SensorLock(switchAfterFrames: 3);
        s.Accept(Sampler.S1, 100.0);                 // lock S1
        for (var i = 0; i < 6; i++)
        {
            Assert.False(s.Accept(Sampler.S2, 2.0));  // stray (even above threshold) → ignored
            Assert.True(s.Accept(Sampler.S1, 100.0)); // live sampler keeps reappearing → resets the counter
        }
        Assert.Equal(Sampler.S1, s.Locked);
    }
}
