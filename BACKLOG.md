# Backlog

Dogfooding feedback and small improvements, batched into releases.

## Open

- **"Always on top" does nothing on Wayland (Pi / labwc)** *(found 2026-07-31)* — the Display
  checkbox sets `Window.Topmost`, which wlroots-based compositors don't honour: there is no Wayland
  protocol for a client to ask to be always-on-top, and labwc ignores the request. Verified on the
  CM5 rather than assumed — a focus window positioned deliberately underneath another application,
  with `AlwaysOnTop: true` in config and the window confirmed mapped via its taskbar entry, still
  drew behind it. A user can tick the box and nothing happens, with nothing saying why.

  *Confirmed working on Windows 11 Pro (v0.7.0-beta, 2026-07-31)*, so this is genuinely
  platform-specific and not a regression in the setting itself — which means the fix is about saying
  so, not about repairing `Topmost`.

  What to settle before implementing: **which condition to test for.** "Wayland" is the wrong
  question — no Wayland compositor offers a client-requestable always-on-top, but an X11 client gets
  `_NET_WM_STATE_ABOVE`, and this app may be running as an X11 client under XWayland rather than as a
  native Wayland one (`.xsession-errors` on the CM5 is full of `xwayland/xwm.c` traffic). So the
  honest test is probably "did the request take effect", not "what is the session type" — and
  labwc's own xwm may or may not honour the hint. Worth checking what Avalonia actually reports for
  the backend before hiding a control on the strength of `$WAYLAND_DISPLAY`. Once known: hide the
  option where it cannot work, or leave it visible and annotated. (`App.axaml.cs` sets `Topmost` in
  `CreateFocusWindow` / `CreateMeterWindow`.)

- **"PEAK FORWARD" doesn't say it is a session high-water mark** *(dogfooding, 2026-07-31)* — it binds
  `SessionPeakW`, a maximum since app start that only ever rises and is cleared solely by Reset Peak.
  So a single high over latches the number, and every later lower-power transmission leaves it
  unchanged. On the CM5 this read an identical `11.2 W` on both meters long after the event that set
  it, which looked exactly like cross-talk between the two meters and prompted an investigation before
  release. *Ruled out on air 2026-07-31:* after resetting both and keying W2 #1 alone, #1 read
  `4.6 W` and #2 stayed `0.0 W` — peak is genuinely per-meter and correct. The number was right and
  the label was misleading. Consider naming it "peak (session)", showing the per-over peak
  (`OverPeakW`, already tracked) alongside it, or timestamping the held value. (`MainWindowViewModel`.)

  *Shaken down again on v0.7.0-beta, 2026-07-31:* with both meters keyed at deliberately different
  powers, the peaks read `41.6 W` (#1, auto-ranged to 200 W) and `11.2 W` (#2, 20 W) — different
  meters, different peaks, which is the case that started this. The combined figure the single focus
  window reports was exercised on real meters in the same session and held the higher of the two, so
  that path is no longer hardware-unverified. **The labelling problem above is still open** — none of
  this changes what "PEAK FORWARD" tells you about which span it covers.

- **Peak, peak-hold and TX-timer logic live in the App layer, untested** — `MeterService` holds
  `SessionPeakW`, the 1.5 s peak-hold ease-down, `OverPeakW` and the TX timer, none of which any of
  the 196 tests touch, contrary to the design rule that non-UI logic belongs in `W2.Core`. It is
  ordinary pure state-machine logic over a reading stream and would port cleanly. A peak-targeting
  bug has already shipped once (v0.4.1-beta's Reset Peak fix). (`MeterService` → `W2.Core`.)

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

- **Registration is skipped on the launch the updater performs (v0.6.2-beta, 2026-07-31).** After
  updating 0.6.1 → 0.6.2 in place, the app was running 0.6.2 while the installed-apps entry still read
  `DisplayVersion 0.6.1-beta`. The key's last-write time was 2026-07-30 17:55:52 — the *previous*
  launch — so the write didn't merely record the wrong value, it never happened. `EstimatedSize` agreed,
  recorded at 101844 KB against an exe now 101846 KB.

  *Not a broken code path.* Launching the same installed binary normally rewrote the entry correctly
  within seconds (`DisplayVersion 0.6.2-beta`, key written 18:32:45). `EnsureRegistered` is
  unconditional, the registry write is the first thing `RegisterWindows` does, and `DisplayVersion`
  reads the running assembly — all correct. It is specifically the updater's relaunch that doesn't get
  there.

  *Why nothing showed.* The failure is silent twice over: `WriteUninstallEntry` returns `false` rather
  than throwing, `EnsureRegistered` ignores the return value, and the startup call is wrapped in
  `catch { /* never block startup over this */ }`. There is no path by which a user or a later session
  learns it didn't happen.

  *Unresolved, and possibly the same fault as the entry that vanished below.* The 0.6.0 → 0.6.1 update
  relaunch **did** write the key (17:55:52, one second after that launch), so it is not simply "update
  relaunches never register" — it worked once and not the next time. Two candidates worth testing
  before assuming a cause: whether the relaunch resolves the exe through a path that makes
  `InstallLayout.Detect` return `Loose` (an 8.3 short path would defeat `SamePath`, which is
  case-insensitive but not length-normalising), and whether something transient right after the exe is
  replaced makes the `reg import` spawn fail.

  *Worth doing regardless of cause:* stop discarding the result. `EnsureRegistered` should surface a
  failed registration somewhere a person or a later session can see — the Updates tab is the natural
  place — so the next occurrence produces evidence instead of forensics. Re-asserting when an update
  completes, rather than only at startup, would also cover the exact launch that missed here.

- **An installed-apps entry was written and then vanished (v0.6.0-beta, 2026-07-30).** After David's
  clean install to `%LOCALAPPDATA%\Programs\W2 Monitor`, the program and the Start Menu shortcut were
  both present but **the HKCU uninstall key was gone** — so the app did not appear in Settings → Apps →
  Installed apps, which is the only route most people have to remove it.

  *It was written first.* The first reading of this was "both registry passes failed", and that is
  wrong — worth stating plainly because the wrong version was briefly recorded here. David accepted the
  install offer, which proves a window owner existed, so the "Installed, but not listed" dialog would
  have appeared had `Install()` returned `Registered: false`. No dialog appeared. `Registered` is
  `wrote && IsRegistered()`, so a `reg query` had to succeed at that moment. The timestamps agree: exe
  copied 17:06:12, shortcut created 17:06:13, and `CreateShortcut` runs only *after* the registry
  writes. The key was therefore present at 17:06:13 and absent by ~17:09.

  *Why nothing healed it.* `EnsureRegistered` checks once at startup and returns early when
  `IsRegistered()` is true. The installed copy was launched by `LaunchDetached` immediately after a
  successful registration, so it saw the key present and skipped. Nothing re-checks after startup, so a
  key that disappears later is never noticed.

  *Ruled out.* The mechanism works: a probe driving `reg.exe` through `ProcessStartInfo.ArgumentList`
  exactly as `RegSet` does wrote all five interesting values, including the two carrying embedded
  quotes (`UninstallString`, `QuietUninstallString`), and an install/uninstall round trip on the *same
  released binary* wrote a correct 11-value entry three minutes earlier. (`reg add` does fail on those
  values from PowerShell, but that is PowerShell's native-call quoting, not the app's path — don't
  chase it.) No Defender detections in the window, and no orphaned uninstall helper in `%TEMP%`.

  *What removed it is unknown.* Deleting the old hand-installed folder afterwards touches no registry.
  No code path in this app deletes that key except `Uninstall`, which was not run.

  *Mitigated, not solved (unreleased).* Registration now rewrites on every launch instead of checking
  and skipping, via `RegFile` (Core, pure, 10 tests) and a single `reg import` rather than eleven
  `reg add` spawns. Verified twice on Windows — once through an adopted legacy folder and once on the
  live install: delete the key, start the app normally, and it is back with every value intact,
  embedded quotes and DWORDs included. So a future disappearance costs one restart rather than being
  permanent and silent. **The cause is still unknown**, and this deliberately does not chase it; if the
  entry starts vanishing repeatedly, that's the signal to look again with the tighter window this now
  gives (it can only have gone missing since the last launch). LP-100A shares the original weakness and
  wants the same change.

  **The evidence is gone** — the entry was repaired by hand so the install would be removable, so a
  fresh reproduction needs a clean install on another machine.

## Planned

Nothing queued. All four items from David's 2026-07-29 list — the connection dots, Avalonia 12, the
self-installer and tabbed Setup — shipped in v0.6.0-beta; the reasoning behind each is kept in Done
below, since most of it is still the reason the code looks the way it does.

The live questions are all in Open: the SSB test on the sampler lock, the missing installed-apps
entry, and the CM5 shakedown of the installer's Linux paths (`HANDOFF-PI.md` carries the test list
for that one).

## Done

- **Reset peak got the meter picker the rest of Setup already used** (v0.7.0-beta) — the button acts
  on the meter selected in Setup, correct since v0.4.1-beta, but it sits on the **Display** tab while
  that selection lives on the **Meters** tab, so nothing on screen named its target; with one window
  open per meter the natural reading was "the meter I'm looking at." Display now carries the same
  `ListBox.picker` bound to the same `SelectedRow` as the W2 Controls and SWR Alarm tabs, so the two
  can't disagree and there's no new state to keep in step. Worth recording the false start: the first
  attempt put the selected meter's *name* on the button instead, which meant a derived label property
  and two `OnPropertyChanged` calls doing a job the existing picker does for free — reach for the
  established control before inventing a second way to say the same thing. The picker sits beside the
  buttons rather than at the head of the tab, because only the reset is per-meter; the display
  toggles around it are global. Gained "Reset all peaks" alongside, which the combined focus-window
  peak needs.

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
