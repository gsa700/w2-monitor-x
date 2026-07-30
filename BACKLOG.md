# Backlog

Dogfooding feedback and small improvements, batched into releases.

## Open

From the 2026-07-17 bug hunt — real findings not fixed in the 0.4.1 batch:

- **Confirm the SensorLock quiet-release constant on air.** The dip-release bug below is fixed, but
  `quietAfterFrames: 4` (~0.8–1 s at the observed 4–5 frames/s) is a reasoned estimate, not a measured
  one — it was validated by unit tests and a replayed SSB envelope, never on a live over. Two things
  to watch on air: a pause longer than ~1 s while still holding PTT will release the lock (a stray
  could then capture the display), and if the release feels sluggish when you swap antennas between
  overs, the switch paths rather than this constant are what to look at. Adjust the constructor
  default if either shows up. (`SensorLock`.)

## Done

- **Minor hardening cluster** (unreleased) — five latent items, and checking them turned up that they
  were not equally real:
  - *Escaping exception on the supervisor thread — real, and the serious one.* `_stop.Wait()` throws
    `ObjectDisposedException` once `_stop` is disposed, `Supervise` had no `catch`, and an unhandled
    exception on a background thread tears down the process. Reachable when `Stop()`'s 3 s join times
    out on a wedged session and `Dispose()` then disposes the event under the still-running loop. Now
    `WaitForStop` treats disposal as "stop", with a catch-all so nothing escapes for any other reason
    either, and `Report` keeps a throwing `StatusChanged` subscriber from doing the same.
  - *`ProbeToggleStates` parsing — real.* Unanchored regex plus `long.Parse` (`TryParse` everywhere
    else), so an overlong digit run threw inside `RunSession`'s try → spurious session teardown and
    reconnect. Deleted the duplicate regex and decoded via `W2FrameParser.Power` instead, which is
    anchored, uses `TryParse`, and is the same decoder the poll loop already trusts.
  - *`SerialDisplay.Shorten` — real.* Leading and trailing "…" shared one condition, so a plain
    over-length raw serial rendered as `…VERYLONGS…`, claiming a by-id extraction that never happened.
    The two marks are now decided independently.
  - *`PowerSwrBar` — real but unreachable in the current layout.* Marker-x went negative when the
    control is narrower than the 3 px marker (narrow the marker, then clamp), and the flash timer
    didn't resume if the control was re-parented mid-alarm (restart it in `OnAttachedToVisualTree`).
  - *`Dispose` idempotency — **not** a live bug.* Repeat `Dispose`/`Stop` never threw: `_stop`'s own
    `Dispose()` is idempotent and its `Set()` tolerates post-disposal calls (both verified). Guarded
    explicitly anyway so that stays true as fields are added, but it fixed nothing observable.

  `SerialReader` also picked up its first tests — 6 hardware-free lifecycle checks. 125 pass (+8).

- **SensorLock released on any sub-threshold dip** (unreleased) — `Accept` dropped the lock the instant
  the locked sampler read ≤ 0.5 W, but SSB/CW power dips below that *within* an over (syllables, CW
  elements), so the lock released mid-over and a stray > 0.5 W on the other sampler could capture the
  display. Release now needs `quietAfterFrames` (4) *consecutive* sub-threshold frames on the locked
  sampler, and any keyed frame resets the run. Replaying a 10 s SSB envelope with a 2 W stray: the old
  rule showed the stray 3× and released mid-over 9×; the new one, zero of each — while a genuine antenna
  swap still switches within `switchAfterFrames`. Constant still wants an on-air confirmation (see Open).
  (`SensorLock`, +4 tests.)

- **Wedged `Open()` under `Guard` can orphan an open port** (unreleased) — an open that exceeded the
  4 s watchdog was abandoned before `_port = port`, so if it later succeeded the handle leaked and the
  next reconnect could hit a self-inflicted "in use." Open and supervisor now hand the port over via
  an atomic claim (`OpenGuarded`): the side that loses the claim closes it, so a late open cleans up
  after itself. Busy-port failure path verified on real hardware (COM7 held by the running app →
  correct access-denied describe + 1 s retry backoff). (`SerialReader.OpenGuarded`, `CloseQuietly`.)

- **`DetectAsync` has no try/catch** (unreleased) — fire-and-forget, so a throw from port enumeration
  or `W2Probe.Detect` left Setup reading "Scanning ports…" forever with no error. Now wrapped; the
  failure lands on the Detect status line in red (`DetectStatusBrush`, mirroring the updater's bound
  brush). (`SetupViewModel`, `SetupWindow.axaml`.)

- **Reconnect status wording — suppress the transient dialout flash** (v0.4.1-beta) — during a
  replug the mid-re-enumeration open would throw `UnauthorizedAccessException` and surface the full
  "Permission denied … sudo usermod -aG dialout" hint for ~1 s, alarming users over a non-problem.
  The reader now tracks whether a session has connected at least once (`_everConnected`); once it
  has, `SerialErrors.Describe(reconnecting: true)` returns a calm `"{port} reconnecting…"` and drops
  the dialout / "another app" hint. A genuine first-connect denial still gets the full guidance.
  (`SerialErrors`, `SerialReader.DescribeRetry`.)


- **Auto-reconnect / follow-the-cable on Linux after a USB drop or renumber** (v0.3.3-beta) —
  the reader now detects a lost link (`LinkHealth`), releases the fd, and reconnects by
  re-resolving `/dev/serial/by-id`; `Open`/`Close` are watchdog-bounded so a surprise-removed
  FTDI can't wedge the thread. Verified on a live CM5 with a forced deauthorize/re-authorize
  that renumbered ttyUSB3→ttyUSB2. (`SerialReader`, `LinkHealth`, `MeterService.ResolveCurrentPort`.)

- **Setup meter list shows the cable serial after the COM port** (v0.3.2-beta) — e.g.
  `W2 #1 · COM4 (A10KMB4VA)`. On Linux the long `/dev/serial/by-id` name is shortened to the
  embedded serial with a leading `…` (e.g. `…A10KMB4VA`) to stay about the Windows length.
  (`SerialDisplay.Shorten` in W2.Core; used by `MeterRow`.)
