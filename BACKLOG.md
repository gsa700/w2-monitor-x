# Backlog

Dogfooding feedback and small improvements, batched into releases.

## Open

- **Decide whether to tell users their W2 firmware is behind** *(deferred 2026-09-04, when the readout
  was added)* — Setup now reports the meter's version but never judges it. Whether that is enough turns
  on a question the beta round is about to answer.

  *Field data so far, and it argues for doing something.* Both station meters run **1.07**, but both
  **shipped from the factory on 1.04** — one bought years ago, the other recently — so 1.04 is what a
  W2 arrives with and is probably what most of them in the wild still run. The Serial Interface
  Commands document is Rev D, April 2010, "applies to firmware rev 1.00 or higher", so there is real
  spread between what ships and what exists. A user on 1.04 would genuinely benefit from being told.

  *What blocks it is honesty, not effort.* There is no machine-readable source for "latest W2
  firmware" — Elecraft distributes it through the W2 Utility, not a feed. So a check can only compare
  against a number baked into the build, and that fails in the worst direction: once it goes stale it
  reports "up to date" to someone who is not, which is worse than saying nothing because it stops them
  looking. Two shapes that stay honest: a constant phrased as *"newest known when this build shipped"*
  and never as *"up to date"*; or a small JSON in this repo fetched the way `UpdateService` already
  fetches releases, which can be corrected without shipping an app release.

  *Wait for the sample.* Two meters from one station, both on the same version, is the worst possible
  basis for this. Testers' meters are the data — if they come back spread across 1.04 to 1.07, build
  it; if everyone is on 1.07, there is nothing to report. (`W2FrameParser.Firmware`, `SetupViewModel`.)

- **Uninstall leaves the single-file extraction directory behind** *(found on the CM5, 2026-08-02)* —
  a self-contained single-file build unpacks its native libraries to `$HOME/.net/<AppName>/<hash>/` on
  Linux (`%TEMP%\.net\…` on Windows), and `Uninstall` knows nothing about it. The hash changes with
  every build, so these accumulate one per distinct binary ever launched and are never reclaimed. On
  this box: **176 MB**, across 7 `W2Monitor` directories and 10 `Lp100aMonitor` ones at 9.6–13 MB
  each, the oldest dated 2026-07-04.

  *Windows is bigger but self-limiting; Linux is smaller and permanent.* Measured on the Windows box
  2026-08-02: **515 MB** in `%TEMP%\.net\`, of which `W2Monitor` is 209 MB over 12 directories and
  `Lp100aMonitor` 263 MB over 15 — all since 2026-07-27, six days. The asymmetry is the part that
  matters for prioritising: on Windows these sit in `%TEMP%`, which Storage Sense and Disk Cleanup can
  reclaim, so the ceiling is bounded by whatever the OS eventually sweeps. On Linux they sit in
  `$HOME/.net/`, which is not temporary and which nothing on the system ever cleans — so the Pi's
  smaller 176 MB only ever grows. **Fix the Linux side first if the two are ever separated.**

  Both counts are inflated by development — a release cycle publishes and smoke-tests several distinct
  binaries in a day. A user updating through the in-app updater accrues one directory per version, at
  roughly 17 MB, which is the number to reason about for the tester round.

  Not a correctness problem — the app runs fine and the directories are inert — but "uninstall the
  program" leaving ~80 MB per app behind isn't what it says on the tin, and **LP-100A is affected
  identically** since the installer pattern is shared. Two things to get right if it's implemented:
  the path is chosen by the .NET host rather than by us, so removing `$HOME/.net/<AppName>` wholesale
  is the honest scope; and a *running* copy has one of those directories open, which is why it belongs
  in the uninstall trampoline beside the install directory rather than in `Unregister`.
  (`InstallService`.)

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

- **RESOLVED by removal in v1.0.0-beta3: Windows' Program Compatibility Assistant virtualises this app's
  registry writes whenever it is launched through the shell** *(proven 2026-09-04)*. Read this before
  touching anything registry-related. It explains every registration anomaly since July, and both
  obvious in-app workarounds have already been tested and shown not to work.

  *Resolution (shipped in v1.0.0-beta3).* The installed-apps entry and everything that wrote it were removed the same day, on
  David's call: install to the same per-user folder, keep the Start Menu and desktop shortcuts (files,
  never affected), and remove from inside the app — a **Remove W2 Monitor…** button on Setup → Updates
  runs the same flow as `--uninstall`. That closes the only real harm, which was a tester with no way
  to uninstall. The registration code is one commit back in history should the exe ever be signed.
  **LP-100A shares the code and the problem and should get the same treatment.** Everything below is
  the investigation record, kept so nobody repeats it.

  *What happens.* When W2Monitor.exe is started from Explorer — desktop shortcut, Start Menu, a
  double-click — or by the updater's PowerShell helper, PCA attaches the `DetectorsAppHealth`
  compatibility layer (visible as `__COMPAT_LAYER=DetectorsAppHealth` in the process environment,
  though it is also applied without that variable). Under it, **every registry write goes to a
  per-launch-tree overlay**: `reg import`, `reg add`, and in-process `RegSetValueEx` alike. Reads
  from the same tree are served from the overlay, so the app writes the installed-apps entry, reads
  the new version straight back, and logs `ok` — while the real key has not changed. The overlay is
  discarded when the tree exits. A child spawned by the layered process inherits the overlay even when
  started by plain `CreateProcess` with `__COMPAT_LAYER` removed. None of it is logged by Defender or
  PCA anywhere a person would look.

  *What it explains.* The entry "written and then vanished" after the 0.6.0 install (July): written to
  the overlay, gone on exit. Every "ok" line in `registration.log` from a shell launch since: real.
  The three consecutive update relaunches that "didn't register": they did, into the overlay. The
  read-back verification added in v1.0.0-beta2 passing while the key stayed stale: it read the
  overlay. Why manual launches from a developer shell always worked: that launch tree was never
  PCA'd. Why the key's last-write timestamp sat a month in the past while the app reported success
  minute by minute.

  *How it was proven.* A 120 ms watcher on the real key recorded zero changes across a shell launch
  whose log line reported a verified write. A self-contained probe reproducing the app's exact steps,
  double-clicked from the Desktop, reported the layer, imported a marker, read it back, and left the
  real key untouched; the identical binary launched from a developer shell had no layer and wrote
  through. The registry-key timestamp instrument was itself validated by writing a value and watching
  it move.

  *Ruled out, with evidence — do not retest:* a different user or hive (same SID); UAC virtualisation
  (allowed, not enabled, both processes); a VirtualStore copy; a duplicate key anywhere in HKCU or
  HKLM; anything reverting the value (watched for 30 s); Defender remediation (no events); ASR (no
  rules configured); an AppCompat `Layers` entry (both hives empty); a compat flag on the shortcuts;
  integrity level (both Medium); TEMP or any other environment difference (PEB compared, only
  Explorer's inherited noise).

  *Levers tested and found NOT to work:*
  - **A `<compatibility>` manifest with `supportedOS` for Windows 10/11** — the documented PCA opt-out.
    Verified as the active `RT_MANIFEST` resource in the installed exe, and separately in a probe with
    no PCA Store entry. Both still received the layer. The section stays in `app.manifest` because it
    is correct hygiene, and its comment says plainly that it does not fix this.
  - **In-process `RegSetValueEx` instead of `reg.exe`** — virtualised identically.
  - **Spawning a child with `__COMPAT_LAYER` scrubbed via `CreateProcess`** — the child inherited the
    overlay and its writes stayed there.
  - **Clearing PCA's Store entries** — pointless: PCA re-adds a program the moment it is launched
    through the shell (both probes appeared in the Store after one run each). The Store records
    monitoring; it does not cause it.

  *The one lever left, untested:* **an Authenticode signature.** On this machine every process
  carrying the layer is unsigned (W2Monitor and all three probes: `NotSigned`) and every
  shell-launched process without it is signed (FlexRadio's CAT and DAX: `Valid`). Manifest, Store
  status and environment do not separate the two groups; a signature does. Testing it needs a code-
  signing certificate, which is a cost and an identity-verification process, so it is David's call.

  *What this means for testers.* Every Windows tester runs an unsigned exe from the shell, so on
  every tester machine the Settings → Apps entry will be missing or stale, and `registration.log`
  will say `ok`. That is cosmetic **except** that Settings → Apps is currently the *only* route to
  uninstall — there is no button in the app. So a tester who wants to remove it has no way to do so
  short of `--uninstall` on a command line they don't know exists. **An uninstall control in Setup is
  therefore required before the beta round, not optional**, and it must not depend on the registry.

  *Honest logging is also possible now.* The app can detect `__COMPAT_LAYER` in its own environment
  and, when present, record that its registry writes are probably virtualised and its own read-back
  cannot be trusted — rather than `ok`. It will miss the launches that get the layer without the
  variable, so it is a partial signal, but a partial true signal beats a confident false one.

  *Severity for the app itself:* nil. The program runs, updates and reads meters regardless; only the
  installed-apps listing is affected. (`InstallService`, `app.manifest`; probe sources in the session
  scratchpad, not the repo.)

  --- *superseded by the above, kept for the reasoning that led there:* ---

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

  *Now three consecutive misses, and black-box observation has run out.* Tracked across four updates
  on the same machine:

  | update | relaunch registered? |
  |---|---|
  | 0.6.0 → 0.6.1 | **yes** — key written 17:55:52, one second after that launch |
  | 0.6.1 → 0.6.2 | no |
  | 0.6.2 → 0.7.0 | no |
  | 0.7.0 → 0.7.1 | no |

  Everything that would explain it mechanically has been checked and doesn't: a manual launch of the
  same installed binary registers correctly and promptly (verified twice), the helper's relaunch is a
  plain `Start-Process -FilePath <exe> -WorkingDirectory <installdir>` with no redirection or altered
  token, and the one release that changed that line (0.6.2's working-directory fix) sits *after* the
  first miss — 0.6.1's helper had no `-WorkingDirectory` and still failed. `Mode` cannot be the
  discriminator either, since it derives from paths that don't vary between launches.

  **Instrumented rather than theorised about further (unreleased).** Every attempt now appends a line
  to `registration.log` beside `config.json` — timestamp, app version, trigger, result, and detail
  specific enough to separate the failure modes: the `reg import` exit code, whether the retry ran,
  whether the verify query disagreed with a write that claimed success. The skip and throw paths, which
  previously produced nothing at all, are recorded too. The updater's helper appends `--updated` when
  it relaunches, so the attempt is logged under the trigger that matters. Surfaced on Setup → Updates,
  shown even when healthy, and it names an entry left on an older version rather than reporting a plain
  success. (`RegistrationLog` in Core, 12 tests; `InstallService`, `App.axaml.cs`, `UpdateApplyScript`.)

  **The log answered it on the 0.8.0 → 0.9.0 update (2026-08-02), and killed both live theories.**

  ```
  01:50:15Z  0.9.0-beta  update   ok  reg import exit 0   <- the call DID run, as "update"
  01:50:35Z  0.9.0-beta  startup  ok  reg import exit 0
  01:55:29Z  0.9.0-beta  startup  ok  reg import exit 0   <- this one actually landed
  ```

  So it was never "the call is skipped" and never "reg.exe refuses". `reg.exe` returns 0 either way;
  at 01:50 the key was untouched afterwards, and at 01:55 an identical call wrote it — the key's
  last-write time matches that line to the second, with `EstimatedSize` moving to the 0.9.0 exe's.

  *Ruled out by direct experiment, so don't re-run these.* No stray or misplaced key exists anywhere
  in HKCU or HKLM. The same `RegFile` output, same UTF-16 BOM, same `reg.exe import` through
  `ArgumentList` lands correctly when run from `dotnet`, from a **self-contained unsigned single-file
  exe**, and from PowerShell — so the caller's identity, the file format, the BOM and the escaping are
  all eliminated. Also worth knowing before it misleads someone: `reg import` writes *"The operation
  completed successfully"* to **stderr**, so stderr text is not a failure signal.

  *What is left is narrower and still unexplained:* `reg.exe` reports success without applying the
  import, under a condition tied to something other than the code path — the only thing that changed
  between the failing and succeeding attempts was that a probe had written the key in between. That is
  a correlation with no mechanism behind it, and three hypotheses have already died here, so it is
  recorded as an observation rather than a theory.

  **Next instrument, whenever a release is being cut anyway:** log the `.reg` file's byte length and
  `reg.exe`'s stderr next to the exit code. A failing attempt would then be distinguishable from a
  working one by evidence rather than by re-deriving it from registry timestamps.

  *Severity is low, so this can wait for a quiet moment:* only `DisplayVersion` and `EstimatedSize` go
  stale. `UninstallString` and `InstallLocation` are path-based and stay correct, so removing the app
  through Settings still works.

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

- **The installer owns a desktop shortcut, on both platforms** (v0.8.0-beta) — `Register` wrote a menu
  entry, an icon and the Linux `~/.local/bin` symlink but nothing on the desktop, which on a Pi is how
  a GUI app actually gets launched. The CM5's shortcut came from the retired
  `install-desktop-shortcut.sh`, so it was unmanaged: it didn't follow an update, `--uninstall` left it
  behind, and once its `Exec` pointed into a deleted `~/Downloads` folder PCManFM stopped treating it
  as a launcher and prompted for confirmation on every launch instead.

  *The open question is settled: unconditional, but never destructive.* It is created whenever nothing
  is already at its path, and an existing file there is left strictly alone — a user may have moved it,
  retargeted it or made their own, and this runs at every launch, so overwriting would undo that
  silently and repeatedly. No `--no-desktop-shortcut` switch: deleting the icon is the opt-out, and it
  is not recreated while any file occupies the path.

  *Legacy launchers are adopted rather than ignored* — `w2monitor.desktop` (no hyphen) from the old
  script is removed and replaced by the installer's own, so the machine ends with one working icon
  instead of one working and one dead. Same duplicate trap `InstallLayout.LegacyFolders` avoids for
  install directories. Uninstall removes both, as named files: the desktop directory is the user's and
  is never swept.

  *`~/Desktop` is not assumed.* The location comes from `XDG_DESKTOP_DIR` in
  `~/.config/user-dirs.dirs`, since the directory is localised and can be switched off; `$HOME/` there
  means "no desktop" by convention and yields no shortcut rather than a file dropped at the top of
  someone's home directory. .NET's `SpecialFolder.DesktopDirectory` is deliberately not trusted on
  Linux — it answers `$HOME/Desktop` whether or not that is true — which is the same kind of
  assumption that produced the v0.7.0-beta symlink bug. Falls back to `~/Desktop` only when it already
  exists. (`XdgUserDirs` in Core, 13 tests; `InstallService`.)

  **Known limitation:** a shortcut sitting at the canonical path is never repointed, only left alone,
  so one that has gone stale there is not repaired — the file cannot be told apart from a user's own.
  The legacy-name case is handled; this one would need a marker to identify the installer's own file.

- **SensorLock holds the sampler lock through a sub-threshold dip** (v0.5.1-beta; **confirmed on air
  2026-07-31**) — closes the last of the 2026-07-17 bug hunt. `Accept` used to drop the lock on the
  first frame at or below the 0.5 W transmit floor, but SSB and CW both fall below it *within* an over,
  so the lock released mid-over and a stray above the floor on the other sampler could capture the
  display. Release now needs four consecutive sub-threshold frames on the locked sampler, and any keyed
  frame resets the run.

  Verification came in two passes, and the first one is the cautionary half: a **carrier** test on
  2026-07-29 exercised only `clearlyStronger` / `lockedWentQuiet`, both untouched by the fix, and would
  have passed identically before it — useful as a regression check that the hold-on bias didn't make
  antenna swaps sluggish, useless as proof of the fix. The gapped-signal test on 2026-07-31 is what
  actually settled it. `quietAfterFrames: 4` (~0.8–1 s at the observed 4–5 frames/s) needs no change.
  (`SensorLock`, +4 tests.)

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

  *Mostly settled on the CM5 since.* The filesystem work has now run on real hardware: install and
  uninstall round-trip against a sandboxed `HOME` (2026-07-31), the `~/.local/bin` symlink created on
  a real install once v0.7.0-beta fixed it, the desktop shortcut and its legacy adoption (2026-08-02),
  and the crash log written by a genuinely failing `--install`.

  *The `sh` trampoline and the `chmod` are settled too (2026-08-02).* A published `linux-arm64`
  single-file build was installed to a sandboxed `HOME`/`XDG_*`/`TMPDIR` and then uninstalled **from
  the installed copy**, which is the arrangement a Debug build can't provide. The generated script was
  captured before it deleted itself:

  ```sh
  #!/bin/sh
  while kill -0 38406 2>/dev/null; do sleep 0.3; done
  rm -rf '<sandbox>/.local/share/w2-monitor'
  rm -f '<sandbox>/tmp/w2monitor-uninstall.sh'
  ```

  Exactly one `rm -rf`, aimed at the install directory alone and shell-quoted — the property worth
  proving, since a shared directory reaching that line is the unrecoverable case. Effects checked:
  the install directory went, all four registration artifacts (menu entry, icon, `~/.local/bin`
  symlink, desktop shortcut) were removed individually, `~/.local/bin`, `~/.local/share/applications`,
  the icon theme and `~/Desktop` all survived, the script removed itself, and the quiet run kept
  settings. `chmod` is exercised on the way in: both the installed executable and the `.desktop`
  shortcut came out `rwxr-xr-x`. What it does *not* clean is the extraction directory — see the
  separate item in Open.

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
  and both phases of the alarm flash. Publish size +5%. Untested at the time: linux-x64 and
  linux-arm64 were cross-published only. *Since settled for arm64* — the CM5 has run published
  single-file builds of v0.7.0/v0.7.1/v0.9.0-beta as its daily driver on two live W2s, and each
  release's arm64 artifact is smoke-tested before upload. **linux-x64 is still cross-published only**
  and has never been launched by anyone here.

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
