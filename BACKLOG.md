# Backlog

Dogfooding feedback and small improvements, batched into releases.

## Open

- **Confirm the SensorLock dip-release on a gapped signal** *(from the 2026-07-17 bug hunt)* — partly verified; the remaining half needs
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

- **The installed-apps entry didn't get written on a real install (v0.6.0-beta, 2026-07-30).** After
  David's clean install to `%LOCALAPPDATA%\Programs\W2 Monitor`, the program and the **Start Menu
  shortcut were both there but the HKCU uninstall key was absent entirely** — not partially written,
  absent. That matters because `RegisterWindows` creates the shortcut *after* both registry attempts,
  so `Register` ran to completion and both `WriteUninstallEntry` passes failed at or before the first
  value. `EnsureRegistered` should then have re-registered on the next launch, and didn't.

  What was ruled out: the mechanism itself works. A probe driving `reg.exe` through
  `ProcessStartInfo.ArgumentList` exactly as `RegSet` does wrote all five interesting values,
  including the two carrying embedded quotes (`UninstallString`, `QuietUninstallString`), and an
  install/uninstall round trip on the *same released binary* had written a correct 11-value entry
  three minutes earlier. (`reg add` does fail on those values from PowerShell, but that's PowerShell's
  native-call quoting, not the app's path — don't chase it.)

  This is the same silent failure LP-100A hit in the field, and the reason the write is already
  verified-and-retried. A plausible unproven cause is a security product blocking a freshly downloaded
  unsigned binary from spawning `reg.exe` against the Uninstall key, which would explain why it works
  from a trusted shell and not from the app. Worth considering a `.reg` file plus a single
  `reg import` instead of eleven separate spawns, which would at least make it one thing to block or
  allow rather than eleven.

  **The evidence is gone** — the entry was repaired by hand on 2026-07-30 so the install would be
  removable, which means a fresh reproduction needs a clean install on another machine.

## Planned

Nothing queued. All four items from David's 2026-07-29 list — the connection dots, Avalonia 12, the
self-installer and tabbed Setup — shipped in v0.6.0-beta; the reasoning behind each is kept in Done
below, since most of it is still the reason the code looks the way it does.

The live questions are all in Open: the SSB test on the sampler lock, the missing installed-apps
entry, and the CM5 shakedown of the installer's Linux paths (`HANDOFF-PI.md` carries the test list
for that one).

## Done

- **Tabbed Setup, as on LP-100A** (v0.6.0-beta) — Meters / W2 Controls / SWR Alarm / Display / Updates,
  each in its own `ScrollViewer` so `MaxHeight` can't clip a control out of reach. The tab header names
  each section, so the in-page ALL-CAPS headings went with it. `SelectedTabIndex` persists via
  `AppConfig.SetupTab` (clamped on load), and opening Setup because of an update selects the Updates
  tab — LP-100A restores the remembered tab in that case, so the window appears with no visible reason
  for it; worth porting this back there. **Fluent styles `TabItem` headers at 24px**, which wrapped
  five of them onto a second line and towered over the 11-13px body text; a local style brings them to
  14px. Verified by screenshotting all five tabs at 150% scaling.

  Note the window still resizes as you switch tabs (`SizeToContent="Height"`, as on LP-100A) — Meters
  is roughly three times the height of Updates. If that reads as jumpy in use, a `MinHeight` on the
  window is the knob.

- **Self-install, ported from LP-100A** (v0.6.0-beta) — `--install` / `--uninstall`, decisions pure and
  tested in Core (`InstallLayout`, `InstallCommandLine`, `DesktopEntry`), side effects in
  `InstallService`. Per-user under `%LOCALAPPDATA%\Programs` as the updater requires; hand-unzipped
  copies adopted where they stand, including this station's `W2Monitor-win-x64`; settings named rather
  than the data directory swept, and kept unless explicitly declined. Verified end to end on Windows —
  install, quiet uninstall, and the offer dialog at 150% scaling — with the live install and config
  untouched throughout.

  Two findings worth carrying elsewhere. **Uninstall deletes `ExeDirectory` only when the copy is
  `Installed`**; LP-100A deletes it unconditionally, which would take out a download folder — or
  Downloads itself, if someone extracted the exe straight into it. Worth porting back there. And
  **`APPDATA` does not isolate this app's config on Windows**: .NET resolves
  `SpecialFolder.ApplicationData` through the known-folder API and ignores the environment variable, so
  the release recipe's smoke-test step claimed a protection that never existed. Corrected in
  `HANDOFF-PI.md`; what actually protects a smoke test is force-killing it before save-on-exit.

  Still open: the Linux half compiles, cross-publishes and is unit-tested, but none of its filesystem
  work has run on real hardware. The CM5 is where that gets settled.

- **Avalonia 11.2.1 → 12.1.1, and the BCL packages the net10 retarget left behind** (v0.6.0-beta) — every
  prediction in the planned entry held, and the LP-100A notes were worth reading first:
  - **Zero source changes.** Build clean, no warnings. Its one deprecation there
    (`TextBox.Watermark`) isn't used here, so a major-version jump cost nothing in code.
  - `Avalonia.Diagnostics` dropped, not bumped (no 12.x; nothing called `AttachDevTools`).
  - **The `Tmds.DBus.Protocol` pin is gone.** Avalonia 12 resolves 0.94.1 transitively, which is
    patched and newer than the 0.21.3 pin — keeping it would now hold the version *down*. The csproj
    comment was rewritten as history rather than deleted, since "why is there no pin here" is the
    question a future reader will have. Vulnerability audit clean on all three projects.
  - `TrimNativeSymbols` ported, and it is load-bearing: `libSkiaSharp.pdb` is 84 MB and
    `libHarfBuzzSharp.pdb` 21 MB in the packages, and they do not bundle into the single file.
    Publish output is now just the exe plus two small managed pdbs.
  - `System.IO.Ports` and `System.Management` → 10.0.10, committed separately from the Avalonia bump.

  Verified beyond build-and-tests, since a renderer major bump is not something a green suite speaks to:
  three RIDs publish; the win-x64 single file launches; serial re-checked on both real W2s (connect,
  decode, connect-time probe); and in `--sim` every `PowerSwrBar` drawing path exercised and screenshotted
  — forward fill, cyan peak marker at the right offset, the SWR gradient (checked against `(swr-1)/2`),
  and both phases of the alarm flash. Publish size +5%. Untested: linux-x64 and linux-arm64 are
  cross-published only, so the CM5 still owes this a real launch before anything ships on it.

- **Setup list's status dots stuck on amber** (v0.6.0-beta) — raised as "make the connection lights green
  rather than orange"; it was a refresh bug, not a colour choice, and the colours are unchanged. Amber
  means *port open, nothing decoded yet* (`StatusIsError ? Red : Current is not null ? Green : Amber`),
  which is exactly what you want to see when the meter is off, the baud is wrong, or the cable is in the
  wrong adapter — so recolouring it would have deleted a real diagnostic and hidden the actual fault.

  *Confirmed by screenshot before touching anything*, both meters connected and live: the Setup rows read
  "Connected on COM7"/"COM3" with **amber** dots while the W2 #1 window showed **green** — same meters,
  same instant, same expression, two answers. Cause: `MeterRow.DotBrush` is recomputed only when
  `MetersChanged` fires, and a reading doesn't raise it (`OnReading` raises it only when the *focus*
  moves), so the rows kept the amber set during `Connect()` for the whole session while the meter window
  updated fine via `FocusReadingUpdated`.

  Fixed with `MeterManager.NoteFirstReading`: raise `MetersChanged` on the `Current` null→non-null edge,
  once per connection rather than at ~4.5 Hz × N meters. That's the only dot input no other event covers
  — connected and error both arrive via `StateChanged`. The flag re-arms on disconnect (which nulls
  `Current`), so a reconnect announces again. Re-screenshotted after: both dots green, layout otherwise
  identical. Refreshing rows per frame was the alternative and would have rebuilt every row's label
  string several times a second for nothing.

- **Minor hardening cluster** (v0.6.0-beta) — five latent items, and checking them turned up that they
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

- **SensorLock released on any sub-threshold dip** (v0.5.1-beta) — `Accept` dropped the lock the instant
  the locked sampler read ≤ 0.5 W, but SSB/CW power dips below that *within* an over (syllables, CW
  elements), so the lock released mid-over and a stray > 0.5 W on the other sampler could capture the
  display. Release now needs `quietAfterFrames` (4) *consecutive* sub-threshold frames on the locked
  sampler, and any keyed frame resets the run. Replaying a 10 s SSB envelope with a 2 W stray: the old
  rule showed the stray 3× and released mid-over 9×; the new one, zero of each — while a genuine antenna
  swap still switches within `switchAfterFrames`. Constant still wants an on-air confirmation (see Open).
  (`SensorLock`, +4 tests.)

- **Wedged `Open()` under `Guard` can orphan an open port** (v0.5.1-beta) — an open that exceeded the
  4 s watchdog was abandoned before `_port = port`, so if it later succeeded the handle leaked and the
  next reconnect could hit a self-inflicted "in use." Open and supervisor now hand the port over via
  an atomic claim (`OpenGuarded`): the side that loses the claim closes it, so a late open cleans up
  after itself. Busy-port failure path verified on real hardware (COM7 held by the running app →
  correct access-denied describe + 1 s retry backoff). (`SerialReader.OpenGuarded`, `CloseQuietly`.)

- **`DetectAsync` has no try/catch** (v0.5.1-beta) — fire-and-forget, so a throw from port enumeration
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
