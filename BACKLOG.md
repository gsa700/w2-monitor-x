# Backlog

Dogfooding feedback and small improvements, batched into releases.

## Open

From the 2026-07-17 bug hunt — real findings not fixed in the 0.4.1 batch:

- **Confirm the SensorLock dip-release on a gapped signal** — partly verified; the remaining half needs
  SSB or CW, not a carrier.

  *Confirmed on air 2026-07-29* (W2 #1, Search mode): carrier on S2 locked the display to S2; keying S1
  while S2 was still live did **not** move it; unkeying S2 handed over to S1 promptly, and alternating
  between them switched fast. So the switch paths (`clearlyStronger` / `lockedWentQuiet`) are prompt and
  the fix's bias toward holding the lock introduced no sluggishness — that was its main regression risk,
  and it's clear.

  *Still outstanding:* that test used a **carrier**, which never crosses the 0.5 W transmit floor, so it
  drove only paths this fix left untouched and would have passed identically before it. The dip case
  needs gaps in the signal — roughly 20 s of slowly-counted SSB (or moderate CW) on one sampler while
  the other is cabled and picking up stray. The `S1`/`S2` label in the status line should hold for the
  whole over; pre-fix it flicked to the idle sampler in the gaps, taking the power and SWR readings with
  it. Then hold PTT through 2+ s of dead air: past about a second the lock releases by design, and a
  stray capturing the display at that point means `quietAfterFrames: 4` is too tight — raise it (6–8) in
  the constructor default.

  *Caveat:* if the idle sampler shows no stray pickup at all while you transmit, this meter cannot
  exhibit the original bug, and the honest result is "not reproducible here" rather than "fixed."
  (`SensorLock`.)

## Planned

David's list, 2026-07-29. His framing was "no particular order"; the order below is a recommendation
with its reasoning attached, not a decision taken. The `lp100a-monitor` cross-references are the point
of half of these — that project is the reference template for the station tools, and two of the four
already exist there in working, tested form.

**Keep the Avalonia bump out of the same release as the SensorLock change.** That fix is still awaiting
its on-air test (see Open), and a renderer bump in the same build would muddy the result.

1. **Connection dots read amber when they should read green — likely a refresh bug, not a palette
   choice.** Amber does not mean "connected"; it means *port open, nothing decoded yet*
   (`StatusIsError ? Red : Current is not null ? Green : Amber`). That is precisely the state worth
   seeing when the meter is powered off, the baud is wrong, or the cable is in the wrong adapter, so
   recolouring it green would delete a real diagnostic rather than fix anything. Suspected cause:
   `MeterRow.DotBrush` refreshes only when `MeterManager.MetersChanged` fires, and a reading doesn't
   fire it — `OnReading` raises it only when the *focus* changes. So the Setup list's dots freeze at
   their last state-change value, which is the amber set during `Connect()` before the first reading
   lands, while the main window's dot updates correctly via `FocusReadingUpdated`. **Confirm first:**
   is the main-window dot green while the Setup list dots stay orange? If so, refresh the rows on
   reading and leave the colours alone. Small; can ride along with the fixes already in `[Unreleased]`.

2. **Avalonia 11.2.1 → 12.1.x, plus the BCL packages the net10 retarget left behind.** LP-100A made
   this jump on 2026-07-28 — read *The .NET 10 + Avalonia 12 migration* in its `CLAUDE.md` before
   starting, because the discovery cost is already paid there:
   - Its only deprecation was `TextBox.Watermark` → `PlaceholderText`. W2 doesn't use `Watermark`
     (checked), so this may be a zero-source-change bump.
   - `Avalonia.Diagnostics` has **no 12.x — drop it, don't bump it.** Nothing here calls
     `AttachDevTools` (only generated build props reference it), so the Debug-only package is dead
     weight. This is also why it alone shows a `11.3.x` "latest" in `dotnet list package --outdated`.
   - **It deletes the `Tmds.DBus.Protocol` pin.** Avalonia 12 pulls 0.94.1 transitively, which clears
     GHSA-xrw6-gwf8-vvr9. Note the csproj comment's "do NOT jump to 0.9x" is correct only *under
     Avalonia 11* — under 12 that is the version Avalonia itself resolves. Rewrite the comment as
     history rather than silently dropping the pin, and re-check with
     `dotnet list package --vulnerable --include-transitive`.
   - **Port the `TrimNativeSymbols` target** from `Lp100a.App.csproj`. Avalonia 12's SkiaSharp and
     HarfBuzzSharp ship native `.pdb` symbols (~101 MB combined) that do *not* bundle into the single
     file — they land loose beside the exe. That matters twice over here, because this app's updater
     contract is a true single file with natives bundled; getting it wrong is the v0.4.0-beta
     crash-on-launch.
   - `System.IO.Ports` and `System.Management` are still at **8.0.0** despite the net10 retarget;
     LP-100A moved both to 10.0.10 in its net10 commit. `System.IO.Ports` is the serial library, so
     Linux/Pi fixes may be sitting in it.

   Split the commits (BCL, then Avalonia) so a regression points at one culprit, revalidate on all
   three platforms, and expect roughly 7% publish-size growth.

3. **Self-install, ported from LP-100A.** `InstallLayout` + `InstallCommandLine` + `DesktopEntry` in
   Core (pure, unit-tested) with the side effects in the App layer — no Inno, WiX or MSI, and no new
   toolchain. See *Self-install (Windows and Linux)* in LP-100A's `CLAUDE.md`.
   - **Per-user under `%LOCALAPPDATA%\Programs` is a constraint, not a preference.**
     `UpdateService.ApplyAndRestart` replaces the running executable in place, which needs no
     elevation there and would need it on every update under `Program Files` — a machine-wide
     installer quietly breaks the updater. This app runs the same updater, so the same constraint
     binds. Don't "fix" the location without re-reading that method.
   - **`LegacyFolders` must cover the hand-unzipped layouts**: `W2Monitor` plus `W2Monitor-win-x64`,
     `-linux-x64`, `-linux-arm64`. The live Windows install sits in
     `%LOCALAPPDATA%\Programs\W2Monitor-win-x64`, so without adoption the first run installs a second
     copy and orphans the one actually in use.
   - **Simpler here than there:** no transmission log. Most of LP-100A's uninstall care is protecting
     `TXlog.csv`; this app has only `config.json` (plus its `.bak`), so the prompts collapse to one.
   - Registry writes go through `reg.exe` deliberately — the registry APIs need `net10.0-windows`, and
     the plain `net10.0` TFM is what lets one target cross-publish Linux and Pi.
   - On uninstall, remove shared-directory items **file at a time**; never delete a directory the app
     doesn't own. On Linux that mistake takes out every user binary on the machine.
   - **Inherits an unverified half:** none of LP-100A's Linux filesystem work — icon extraction,
     `.desktop` write, symlink, `chmod`, the `sh` uninstall trampoline — has run on real hardware. This
     app is better placed to settle it, since it already runs on the CM5.

4. **Tabbed Setup, as on LP-100A** (`Lp100a.App/Views/SetupWindow.axaml`: Connection / Display / Alarm
   / Logging / Updates). The existing sections here map almost one-to-one onto **Meters / W2 Controls /
   SWR Alarm / Display / Updates**, and there's room — this `SetupWindow.axaml` is 122 lines against
   LP-100A's 198. Independent of the installer, which drives off a first-run prompt and the command
   line and adds no Setup UI at all. Cheapest of the four and genuinely "whenever", but worth doing
   *after* the Avalonia bump so Setup isn't laid out twice.

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
