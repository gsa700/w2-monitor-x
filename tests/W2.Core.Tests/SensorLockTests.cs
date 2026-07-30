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
    public void Releases_once_the_over_has_really_ended()
    {
        var s = new SensorLock(quietAfterFrames: 4);
        s.Accept(Sampler.S1, 100.0);                // lock S1
        Assert.False(s.Accept(Sampler.S2, 0.5));    // stray ignored

        for (var i = 0; i < 3; i++)                 // quiet, but not yet long enough to be an ended over
        {
            Assert.True(s.Accept(Sampler.S1, 0.0));
            Assert.Equal(Sampler.S1, s.Locked);
        }

        Assert.True(s.Accept(Sampler.S1, 0.0));     // 4th consecutive quiet frame → release
        Assert.Equal(Sampler.Unknown, s.Locked);
        Assert.True(s.Accept(Sampler.S2, 0.5));     // now free to follow either
    }

    [Fact]
    public void A_syllable_gap_does_not_hand_the_display_to_a_stray()
    {
        // The reported bug: SSB/CW power dips below the transmit floor *within* an over — between
        // syllables, and between CW elements. Releasing on that first quiet frame let a stray above
        // the floor on the other sampler capture the display mid-over.
        var s = new SensorLock();
        Assert.True(s.Accept(Sampler.S1, 100.0));   // lock S1 on the over
        Assert.True(s.Accept(Sampler.S1, 0.0));     // syllable gap — still our over
        Assert.Equal(Sampler.S1, s.Locked);
        Assert.False(s.Accept(Sampler.S2, 2.0));    // stray on the idle sampler → still ignored
        Assert.True(s.Accept(Sampler.S1, 95.0));    // voice resumes on the real sampler
        Assert.Equal(Sampler.S1, s.Locked);
    }

    [Fact]
    public void Intermittent_dips_never_accumulate_to_a_release()
    {
        // CW: element gaps alternate with elements for the whole over. The quiet run has to reset on
        // every keyed frame — counting them cumulatively would release the lock mid-stream.
        var s = new SensorLock(quietAfterFrames: 4);
        s.Accept(Sampler.S1, 100.0);
        for (var i = 0; i < 20; i++)
        {
            Assert.True(s.Accept(Sampler.S1, 0.0));     // gap
            Assert.True(s.Accept(Sampler.S1, 100.0));   // element
        }
        Assert.Equal(Sampler.S1, s.Locked);
    }

    [Fact]
    public void Holding_through_a_dip_still_lets_rf_move_within_switch_after()
    {
        // The bias toward holding on must not delay a genuine move: S1 stops keying exactly as S2
        // starts, and the switch path — not the release path — is what follows the RF over.
        var s = new SensorLock(switchAfterFrames: 3, quietAfterFrames: 4);
        s.Accept(Sampler.S1, 100.0);                 // lock S1
        Assert.True(s.Accept(Sampler.S1, 0.0));      // S1 stops — quiet 1, lock still held
        Assert.False(s.Accept(Sampler.S2, 60.0));    // S2 keys — locked sampler quiet 1 frame
        Assert.False(s.Accept(Sampler.S2, 60.0));    // 2
        Assert.True(s.Accept(Sampler.S2, 60.0));     // 3 → follow the RF to S2 despite the held lock
        Assert.Equal(Sampler.S2, s.Locked);
    }

    [Fact]
    public void The_next_over_on_the_other_antenna_captures_after_release()
    {
        var s = new SensorLock(quietAfterFrames: 4);
        s.Accept(Sampler.S1, 100.0);
        for (var i = 0; i < 4; i++) s.Accept(Sampler.S1, 0.0);   // over genuinely ends
        Assert.Equal(Sampler.Unknown, s.Locked);
        Assert.True(s.Accept(Sampler.S2, 80.0));                 // key the other antenna → locks S2
        Assert.Equal(Sampler.S2, s.Locked);
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
