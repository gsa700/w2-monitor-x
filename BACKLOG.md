# Backlog

Dogfooding feedback and small improvements, batched into releases.

## Open

From the 2026-07-17 bug hunt — real findings not fixed in the 0.4.1 batch:

- **SensorLock releases on any sub-threshold dip (needs on-air check).** `SensorLock.Accept` drops
  the lock the instant the locked sampler reads ≤ 0.5 W ("over ended → release"). SSB/CW power dips
  below that between syllables/elements *within* an over, so the lock can release mid-over and a
  stray > 0.5 W on the other sampler can then capture the display. Fix (if it visibly flickers on
  air): require a few consecutive sub-threshold frames before releasing, mirroring `_switchAfter`.
  Validate on real hardware before touching the tuned logic. (`SensorLock.cs:57`.)
- **Wedged `Open()` under `Guard` can orphan an open port.** If `SerialPort.Open()` exceeds the 4 s
  watchdog, `RunSession` returns before `_port = port`, but the abandoned thread still holds the
  port; if that open later succeeds the handle leaks (only GC closes it) → the next reconnect can
  hit a self-inflicted "in use." Have the guarded closure close its own port if it completes after
  being abandoned. (`SerialReader.cs:146-166`.)
- **`DetectAsync` has no try/catch.** Fire-and-forget; if `W2Probe.Detect` throws, Setup shows
  "Scanning ports…" forever with no error. Wrap in try/catch → error status. (`SetupViewModel`.)
- **Minor hardening (latent / low):** `SerialReader.Dispose` isn't idempotent and an unhandled
  exception can escape the supervisor thread if `Stop()`'s 3 s join times out; `ProbeToggleStates`
  uses an unanchored regex + `long.Parse` (vs `TryParse` elsewhere); `SerialDisplay.Shorten`
  prepends a misleading "…" to over-length raw serials; `PowerSwrBar` marker-x can go negative /
  flash timer won't restart if the control is ever re-parented (not reachable in the current layout).

## Done

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
