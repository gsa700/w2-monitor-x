# Changelog

Cross-platform **W2 Monitor** (.NET 10 + Avalonia). Companion to the original PowerShell
app; this is the Windows/Linux/Raspberry-Pi rewrite.

## [Unreleased]

### Removed
- **The Windows installed-apps entry, and everything that wrote it.** The app no longer appears in
  Settings → Apps → Installed apps, on purpose. It used to register itself there, and for two months the
  entry kept turning up missing or stale while the app's own log insisted it had been written. The cause
  was found on 2026-09-04: whenever the app is launched from Explorer or by the updater's helper, Windows'
  Program Compatibility Assistant attaches a compatibility layer to this unsigned exe that virtualises
  **every** registry write — reg.exe, in-process API calls, and any child process — into an overlay the
  app reads back consistently and loses on exit. So it wrote the entry, verified it, reported success,
  and the real registry never changed. The documented manifest opt-out, in-process writes and a
  self-relaunch with the layer stripped were each tested and none escaped it. The only untested lever is
  a code-signing certificate. Rather than ship a feature that reports success while doing nothing, it was
  removed: `RegFile`, `RegistrationLog`, `registration.log`, the reg.exe plumbing and their 22 tests are
  gone. Start Menu and desktop shortcuts are files, were never affected, and stay. Linux is untouched.

### Added
- **Remove W2 Monitor… in Setup → Updates.** Removal now lives in the app, where it depends on nothing
  outside it: the same confirm-and-clean-up flow `--uninstall` runs, including the question about
  keeping your settings. Shown for an installed copy only. On Windows this is now the way to uninstall;
  `--uninstall` still works from a command line, and Linux keeps its menu entry as before.

### Changed
- The install offer says plainly that the app will not appear in Settings → Apps and where to remove
  it from, rather than leaving that to be discovered.
- The updater's helper no longer passes `--updated` on relaunch; nothing reads it now. Older helpers
  that still pass it are harmless, since unknown arguments are ignored.

## [1.0.0-beta2] - 2026-09-04

One fix, in the machinery that reports whether this copy is listed in Settings → Apps → Installed apps.

### Fixed
- **Registration is verified against what actually landed, not against the key merely existing.** The
  check asked whether `DisplayName` was present — and it was, left over from an earlier release — so an
  import that reported success and changed nothing still passed, and the diagnostic log recorded "ok"
  for a write that never happened. It now reads `DisplayVersion` back and compares it to the running
  version, and records what reg.exe itself printed rather than only its exit code. The intermittent
  failure this exists to catch is not currently reproducible; what changed is that the next occurrence
  will say so instead of claiming success. (`InstallService`.)
- **Console tools are drained before being waited on.** Both output streams were redirected and never
  read, which is the shape that deadlocks a parent when a child prints more than the pipe buffer holds.
  reg.exe prints far too little for that to have bitten, which is exactly why it was worth fixing while
  it was still cheap.

## [1.0.0-beta1] - 2026-09-04

The first 1.0 candidate, and the first build meant for stations other than the one it was written on.
Feature work is essentially done; what follows depends on what testers find.

### Fixed
- **The update check understands pre-release versions.** It compared versions by truncating at the
  first dash, which was fine while every release was `X.Y.Z-beta` and the numbers always moved. Under
  a `-beta1`, `-beta2` scheme the numbers hold still and the suffix carries the difference, so
  `1.0.0-beta1`, `1.0.0-beta2` and `1.0.0` all compared equal — anyone on beta1 would have been told
  they were up to date forever, including once the real 1.0 shipped. Ordering now follows semantic
  versioning: a pre-release ranks below the release it precedes, and trailing digits compare as
  numbers, so `beta10` follows `beta2` instead of preceding it. (`VersionOrder` in W2.Core, 25 tests.)

  Upgrading from any earlier release is unaffected — 0.9.0-beta and before are ordered by their
  numbers, which still move.

### Added
- **The meter's own firmware version is shown in Setup → Updates.** Read with the W2's `V` command once
  per connection — the manual marks it "EEPROM: No", so it writes nothing and can be asked on every
  connect, including mid-transmission. Each connected meter is listed with its version. The app
  deliberately does **not** offer to update it, and says why: a W2 only accepts a firmware load when it
  is powered on with the SENSOR button held, so no software on a normal serial connection can flash it.
  It names Elecraft's free W2 Utility and links to their site instead. Verified against both station
  meters, which report 1.07.

## [0.9.0-beta] - 2026-08-02

Groundwork for putting the app in front of testers: a crash now leaves something behind that can be
sent, and the app says so rather than leaving the file to be discovered. Nothing changed on the serial
or protocol side.

### Added
- **A crash leaves a report behind.** Unhandled errors are now written to `crash.log` beside
  `config.json` — timestamp, app version, platform and the full stack including inner exceptions —
  and the README tells people where to find it and to attach it to an issue. Until now a crash left
  nothing: on Linux the stack went to stderr, which for an app launched from the desktop menu goes
  somewhere nobody will look, and on Windows it went nowhere at all, so the best report anyone could
  give was "it closed." Handlers are attached before Avalonia starts, so a failure during startup —
  the one a tester on an unfamiliar distribution is most likely to hit — is caught too, along with
  faulted background tasks and a failed `--install`, which previously reported only an exit code.
  The file keeps its last few reports; the tidy-up runs at startup rather than at crash time, since
  a dying process should only append. (`CrashReport` in W2.Core, 16 tests; `CrashLog`.)
- **Setup → Updates says when a previous run crashed**, and offers **Show crash log** to reveal the
  file ready to attach. Without it the log is only discoverable by rereading the README at exactly
  the wrong moment. The notice appears only when there is something to report, so a machine that has
  never crashed shows nothing, and it names the file rather than describing the fault — someone who
  has to be told the log exists also has to be told what it is called.

## [0.8.0-beta] - 2026-07-31

A desktop shortcut the installer actually maintains, and an end to guessing about why the Windows
installed-apps entry sometimes goes stale.

> **Shipped with a caveat, since settled.** At release the Linux half had not run on real hardware —
> the XDG desktop-directory lookup and the `.desktop` write were unit-tested and cross-published but
> untried on a Pi. *Verified on the CM5, 2026-08-02, under v0.9.0-beta:* `XDG_DESKTOP_DIR` was read
> rather than `~/Desktop` assumed, the shortcut was written with its executable bit and a target that
> resolves, and the legacy `w2monitor.desktop` was adopted and removed on a machine that actually had
> one — the adoption path met a real stale file rather than a synthetic one. `registration.log`
> recorded it: `ok — entry already current; desktop shortcut created, legacy one removed`.

### Added
- **The installer puts a shortcut on your desktop.** Previously it wrote a menu entry, an icon and the
  Linux `~/.local/bin` symlink but nothing on the desktop — which on a Raspberry Pi is how a GUI app
  actually gets launched. The shortcut on the CM5 came from a script that shipped in the pre-installer
  zips and no longer exists, so nothing maintained it: it didn't follow an update, uninstall left it
  behind, and once it pointed at a deleted download folder the file manager stopped treating it as a
  launcher and asked for confirmation on every launch instead.

  It is created whenever nothing is already at its path, and **an existing shortcut is left strictly
  alone** — you may have moved it, retargeted it or made your own, and this runs at every launch, so
  overwriting would undo that silently and repeatedly. There is no opt-out switch because none is
  needed: delete the icon and nothing puts it back. An old `w2monitor.desktop` from the retired script
  is replaced by the installer's own, so you end up with one working icon rather than one working and
  one dead, and uninstall removes both.

  On Linux the location comes from `XDG_DESKTOP_DIR`, not a hardcoded `~/Desktop` — the folder is
  localised, and a user can switch it off entirely, in which case no shortcut is created rather than a
  file being dropped at the top of your home directory.
- **Setup → Updates reports whether this copy is listed in the OS's installed-apps list**, for an
  installed copy. Shown even when it is fine, because the fault it exists to catch is one where
  nothing appears to be wrong, and it says so plainly when the entry was last written by an older
  version than the one running.

### Changed
- **Registration now leaves a record of every attempt**, in `registration.log` beside `config.json`.
  After an in-place update the installed-apps entry has sometimes kept the *previous* version, and
  nothing on the machine distinguished "the call was skipped" from "Windows refused it" from "it threw
  first" — diagnosing it meant reading a registry key's last-write timestamp and reasoning backwards.
  Each attempt now records its trigger, result and enough detail to tell those apart. Nothing about
  this changes what the app does; it changes what it can tell you afterwards.

## [0.7.1-beta] - 2026-07-31

A Setup tidy-up on top of v0.7.0-beta, from dogfooding it on the CM5 the same evening. One change,
and partly cleaning up after the last release: v0.7.0-beta's peak-reset controls landed between the
TX timer's checkbox and the timeout that governs it, which made an existing disconnect worse.

### Changed
- **Setup → Display now groups each readout with the settings that govern it.** The TX timer's
  show/hide checkbox sat in the toggle grid while the timeout it sets sat at the foot of the tab,
  with nothing connecting the two — and the peak-reset controls landing between them in v0.7.0-beta
  widened the gap. The tab is now a plain toggle grid, then a **TX TIMER** section holding its
  checkbox, its timeout and a note on what the readout does at the limit, then a **PEAK FORWARD**
  section holding its checkbox and the reset picker and buttons. Section headers follow the
  `SELECTED METER — PORT` style already used on the Meters tab.

## [0.7.0-beta] - 2026-07-31

Shaken down on a Raspberry Pi CM5 with two W2s connected. The headline is that the Linux install
finally creates its `~/.local/bin` symlink — it never had, on any launch, since the self-installer
landed in v0.6.0-beta. The rest came out of chasing a suspected peak bug that turned out not to be
one: two meters reporting an identical peak looked like cross-talk, but keying one meter alone showed
the readings were per-meter and correct all along. What was actually wrong was how little the display
said about the number it was showing.

### Added
- **"Reset all peaks"** on Setup → Display, beside the per-meter reset and shown only when more than
  one meter is configured. The focus window now reports a figure drawn from every connected meter, so
  there had to be one action that clears what it is actually showing.

### Changed
- **The single focus window reports the highest peak among the connected meters, not just the focused
  meter's.** That window follows whichever meter is keying, so a peak belonging only to the focused
  meter appeared to fall the moment focus moved to a quieter one. Per-meter windows are unchanged —
  each still shows its own meter's peak, which is the whole point of a window dedicated to one meter.
  Only the printed figure combines: the cyan peak-hold marker stays the focused meter's, because the
  bar beneath it is that meter's live forward power and a marker from another meter would point at
  nothing. Disconnected meters are excluded, so unplugging the meter holding the maximum lowers the
  figure — deliberately, since this is the peak across what is being measured now rather than a high
  score for the session. (`PeakPolicy` in W2.Core, unit-tested.)

### Fixed
- **"Reset peak forward" now shows which meter it will reset.** It acts on the meter selected in
  Setup, which has been correct since v0.4.1-beta, but the button sits on the **Display** tab while
  that selection lives on the **Meters** tab — so nothing on screen said which meter it would hit,
  and with one window open per meter the natural reading was "the meter I'm looking at." Display now
  carries the same meter picker the W2 Controls and SWR Alarm tabs use, bound to the same
  `SelectedRow`, so the choice cannot drift. It sits beside the reset buttons rather than at the head
  of the tab, because only those are per-meter — the display checkboxes and the TX timeout are
  global. The button is also disabled when no meter is selected.
- **The `~/.local/bin/w2-monitor` symlink is created on Linux.** It never was — not on first install,
  not on any later launch. The installer probed for an existing link with `File.ResolveLinkTarget`
  before deciding whether to create one, and that call throws `FileNotFoundException` when nothing is
  at the path at all, which is precisely the first-install case. Since that derives from
  `IOException`, the throw landed in the handler meant for a failed *creation* and the create was
  skipped every time. Found on the CM5, where the `.desktop` entry and the hicolor icon were both in
  place and the symlink was not. Probing now lives in `W2.Core.Symlink`, where a missing path is an
  answer rather than an exception, so the `catch` in `InstallService` guards only the creation.

## [0.6.2-beta] - 2026-07-31

One fix, in the updater itself. Worth taking even though the failure is narrow, because it is the
update path that carries every future fix.

### Fixed
- **Updating twice without restarting in between no longer fails, and each update stops leaving ~100 MB
  behind.** The apply helper relaunched the app with no working directory of its own, so the new process
  inherited the helper's — the temp staging folder holding the unpacked release. Windows won't delete a
  directory that is some process's working directory, so the staging folder survived every update; and
  since an update begins by clearing that same folder, a second update in one session would have thrown
  on it. The helper now starts the app in the install directory and lives in the temp root rather than
  inside the folder it has to remove, because a script cannot delete the directory it is sitting in.
  Ported from LP-100A Monitor, which hit this first. (`UpdateService`, `UpdateApplyScript`.)

## [0.6.1-beta] - 2026-07-30

Follow-up to 0.6.0-beta: the tabbed Setup as it should have shipped, and the Windows installed-apps
entry made able to repair itself. No new features, and nothing changed on the serial or protocol side.

### Changed
- **Setup's tabs look like tabs.** Fluent draws them as large underlined text, which against this dark
  palette read as a row of labels — outlined and rounded now, with the selected one filled and merged
  into the panel below it, matching LP-100A.
- **Setup no longer changes height when you switch tabs.** It's fixed at the length of the longest tab
  plus a little, which costs some empty space at the bottom of the short ones and is much less
  distracting than a window that jumps every time you click.
- **The tabs that act on a meter now say which one, and let you change it.** W2 Controls and SWR Alarm
  each carry a meter picker; it shares its selection with the Meters tab, so the two can't disagree.

### Fixed
- **Setup no longer keeps the app alive after you close the main windows.** It's a top-level window, so
  closing every meter window left it sitting there — with nothing to configure and a process that never
  exited. It now closes with the last one.
- **The Windows installed-apps entry repairs itself.** After a clean install of 0.6.0-beta the entry
  was written correctly and then went missing, so the app stopped appearing in Settings → Apps →
  Installed apps — the only route most people have to uninstall it. What removed it is still unknown,
  but nothing noticed, because the check ran once at startup and the freshly installed copy had seen a
  perfectly good entry a second earlier. Registration is now re-asserted on every launch, so an entry
  lost at any point comes back the next time the app starts. It is also written as a single
  `reg import` rather than eleven separate `reg` commands, which is both cheap enough to repeat and
  one action for a security product to allow or block rather than eleven independent ones.

## [0.6.0-beta] - 2026-07-30

Two new features and a major dependency jump. **The Linux and Raspberry Pi side of the installer has
never run on real hardware** — it compiles, cross-publishes, and its pure logic is unit-tested, but no
part of its filesystem work (icon, `.desktop` entry, symlink, `chmod`, the uninstall trampoline) has
executed on a Linux box. On Linux, treat this release as the thing that finds that out. Windows is
verified end to end.

### Added
- **Setup is tabbed** — Meters / W2 Controls / SWR Alarm / Display / Updates, matching LP-100A Monitor.
  The single stack had grown long enough that every new option pushed the window taller, and the
  bottom sections were off the bottom of shorter screens. Each tab scrolls independently, the tab
  showing when you close Setup is the one you get back, and opening it because an update is waiting
  lands on Updates rather than wherever you last were.
- **The app installs itself** — `--install` / `--uninstall`, no Inno/WiX/MSI and no new toolchain,
  ported from LP-100A Monitor. A copy run from wherever it was unzipped offers to install; it lands in
  `%LOCALAPPDATA%\Programs\W2 Monitor` (or `~/.local/share/w2-monitor`) and appears in Settings → Apps
  → Installed apps with a Start Menu shortcut, or in the applications menu on Linux with a
  `~/.local/bin/w2-monitor` symlink. **Per-user is required, not chosen:** the in-app updater replaces
  the running executable in place, which would need elevation on every update under `Program Files`.
  A copy already installed by hand — this station's is `…\Programs\W2Monitor-win-x64` — is adopted
  where it stands rather than duplicated. Put a `portable.txt` beside the program to be left alone.
  Uninstall keeps your settings unless you say otherwise, and only ever deletes a directory the app
  owns. Windows verified end to end; the Linux paths are cross-published and unit-tested but have not
  yet run on real hardware.

### Changed
- **Avalonia 11.2.1 → 12.1.1.** A major-version jump that needed **no source changes at all** — build
  clean with zero warnings. LP-100A made the same jump first and hit exactly one deprecation
  (`TextBox.Watermark` → `PlaceholderText`), which this app never used. Verified on Windows against
  both real W2s and, in `--sim`, across every drawing path of the custom `PowerSwrBar`: forward fill,
  cyan peak-hold marker, the SWR gradient, and both phases of the alarm flash.
- **`Avalonia.Diagnostics` dropped rather than bumped** — it has no 12.x release, and nothing here ever
  called `AttachDevTools()`, so the Debug-only reference was dead weight.
- **`System.IO.Ports` and `System.Management` 8.0.0 → 10.0.10.** Both were left behind by the .NET 10
  retarget in 0.5.0-beta, which moved the target framework but not the packages. Serial re-verified on
  real hardware afterwards: both meters connect, decode, and resolve the connect-time state probe.
- Publish size grew about 5% (win-x64 99 → 104 MB, linux-x64 95 → 99, linux-arm64 101 → 106).

### Fixed
- **Setup's meter list now shows a connected meter as green, not amber.** The dot means three things —
  red for an error, amber for "port open but nothing decoded yet", green for live data — and the amber
  state is worth seeing when the meter is powered off or the cable is in the wrong adapter. But the rows
  were only ever repainted when the meter list or a connection state changed, and a *reading* is neither,
  so they kept the amber set at connect for the whole session while the main window's dot correctly went
  green. The list is now told once per connection when frames start arriving. (`MeterManager`.)
- **A reader fault can no longer take the app down with it.** `Supervise` ran without a `catch`, so an
  exception on that background thread was unhandled — which in .NET means the whole process exits. The
  reachable trigger: `Stop()`'s 3 s join times out on a wedged session, `Dispose()` disposes the stop
  event underneath the still-running loop, and its next `Wait()` throws. Waits now treat a disposed
  event as "stop", a catch-all guarantees nothing escapes the thread whatever the cause, and a throwing
  `StatusChanged` subscriber can't do it either. (`SerialReader`.)
- **A malformed F reply no longer drops the connection.** The connect-time state probe carried its own
  copy of the forward-power format, unanchored and using `long.Parse` where the rest of the codebase
  uses `TryParse` — so an overlong digit run threw, and the throw surfaced as a session teardown and
  reconnect. The copy is gone; it now decodes through `W2FrameParser`, the same anchored decoder the
  poll loop uses. (`SerialReader.ProbeToggleStates`.)
- **A long serial no longer reports a cable identity it doesn't have.** In the Setup meter list, the
  leading "…" means "serial extracted from a long `/dev/serial/by-id` name" and a trailing one means
  "truncated" — but both came from a single condition, so an over-length raw serial displayed as
  `…VERYLONGS…`, implying a Linux by-id extraction that never took place. (`SerialDisplay.Shorten`.)
- **Peak-hold marker and alarm flash edge cases.** The marker could be drawn at a negative offset when
  the control is narrower than the marker itself, and the alarm flash didn't resume if the bar was
  re-parented mid-alarm. Neither is reachable in the current window layout. (`PowerSwrBar`.)

### Security
- **The hand-rolled `Tmds.DBus.Protocol` pin is gone, and the vulnerability stays fixed.** 0.5.0-beta
  pinned 0.21.3 by hand to patch [CVE-2026-39959] on the Linux and Raspberry Pi builds. Avalonia 12
  resolves 0.94.1 through `Avalonia.FreeDesktop` on its own, which is also patched and *newer* than the
  pin — so keeping the pin would now hold the version down rather than up. `dotnet list package` with
  the vulnerable and include-transitive switches reports clean across all three projects.

### Build
- **Native debug symbols are no longer published.** Avalonia 12 pulls SkiaSharp and HarfBuzzSharp builds
  that ship `.pdb` symbols (`libSkiaSharp` 84 MB, `libHarfBuzzSharp` 21 MB) which, unlike the natives
  themselves, are *not* bundled into the single file — they land loose beside the executable. That would
  have roughly doubled every release zip and broken the one-self-contained-exe assumption the in-app
  updater depends on, since it copies only the exe. A publish target drops them; the managed `.pdb`s are
  tiny and stay, so crash traces from this app's own code still resolve. (`W2.App.csproj`.)

### Internal
- `SerialReader` gained its first unit tests — 6 lifecycle checks that need no serial port — and
  `Dispose` is now explicitly idempotent. That last one was hygiene, not a fix: repeat disposal was
  already harmless, since the underlying event tolerates both double-`Dispose` and post-`Dispose`
  `Set()`. 125 tests pass.

## [0.5.1-beta] - 2026-07-29

Three fixes from the 2026-07-17 bug hunt, closing out that batch. No feature or protocol changes.

### Fixed
- **Search-mode lock no longer drops out on a syllable gap.** The sampler lock released the moment the
  locked sampler read at or below the 0.5 W transmit floor — but SSB and CW both dip below that *within*
  an over, between syllables and between CW elements. The lock could therefore release mid-over, and a
  stray above the floor on the other sampler would capture the display: the exact flicker the lock exists
  to prevent. Releasing now takes four *consecutive* sub-threshold frames on the locked sampler (~0.8–1 s
  at the observed 4–5 frames/s), and any keyed frame resets the run. Releasing late costs little, since a
  genuine antenna swap is followed by the existing switch paths rather than by the release. Replaying a
  10 s SSB envelope against a 2 W stray, the old rule handed the display to the stray 3 times and released
  mid-over 9 times; the new rule does neither. (`SensorLock`.)
- **A slow serial open can no longer orphan the port.** `Open()` runs under a 4 s watchdog so a
  stale/removed FTDI can't stall the reconnect loop, but a wedged open that *later succeeded* left an
  open handle nobody referenced — no field pointed at it, so only the finalizer would close it, and
  the next reconnect attempt could hit a self-inflicted "port in use." The open and the supervisor
  now hand the port over through an atomic claim: whichever side loses the claim closes it, so a
  late-completing open cleans up after itself. A failed open also disposes its `SerialPort` instead
  of dropping it on the floor. (`SerialReader.OpenGuarded`.)
- **A failing Detect no longer hangs Setup on "Scanning ports…" forever.** `DetectAsync` is
  fire-and-forget, so an exception from port enumeration or the probe was swallowed with the status
  line stuck mid-scan and no error anywhere. It now reports `Detect failed: <reason>` in red — the
  Detect status line got a bound brush to do it, matching the updater's. (`SetupViewModel`,
  `SetupWindow.axaml`.)

## [0.5.0-beta] - 2026-07-20

### Changed
- **Retargeted from .NET 8 to .NET 10 (LTS).** .NET 8 reaches end of support on 2026-11-10; .NET 10
  is supported through November 2028. The self-contained builds now bundle the .NET 10 runtime, so
  users still install nothing — the change is transparent at runtime. Verified on Windows and on the
  Raspberry Pi CM5 (linux-arm64); build clean, 113/113 tests, all three RIDs publish and run.

### Security
- **Linux/Pi: patched a high-severity D-Bus vulnerability** ([CVE-2026-39959], CVSS 7.1). Avalonia's
  Linux backend pulled in `Tmds.DBus.Protocol` 0.20.0 transitively, where a malicious D-Bus peer on
  the same session could spoof signals by impersonating name owners, exhaust file descriptors, or
  crash the app with a malformed message body. Now pinned to the patched 0.21.3. Affects the
  `linux-x64` and `linux-arm64` (Raspberry Pi) builds only — Windows doesn't use D-Bus. The pin can
  be dropped once Avalonia's own floor moves past 0.21.3. (`W2.App.csproj`.)

  Worth noting how this hid: the **.NET 8 SDK** audits only *direct* NuGet packages, so the build
  reported nothing; the **.NET 9+ SDK** audits transitively and flags it. (It's the SDK that
  decides, not the target framework — building the same `net8.0` source with the .NET 10 SDK is
  what surfaced it.) Avalonia 11.2.1 was pinned in the Phase 0 scaffold and never bumped, so every
  release to date — 0.2.0-alpha through 0.4.1-beta — shipped the vulnerable version on Linux.

[CVE-2026-39959]: https://github.com/advisories/GHSA-xrw6-gwf8-vvr9

### Added
- **The Elecraft W2 reference PDFs now live in `docs/`** — the owner's manual, the serial interface
  command reference, and the power-on mod. These are the primary sources the `W2.Core` protocol
  layer was built from; they were previously outside the repo, unconnected to the code implementing
  them.

### Build
- **Pinned the build SDK to .NET 10 via `global.json`** (`rollForward: latestMinor`). All three
  build machines (Windows, Raspberry Pi CM5, Fedora) now use the same SDK major, so analyzer,
  NuGet-audit, and package-resolution behavior can't silently differ between them — the exact class
  of drift that let the D-Bus CVE above go unreported. Pinned to 10 rather than 8 on purpose: the
  .NET 8 SDK would switch transitive auditing back off. The Pi and Fedora boxes need the .NET 10 SDK
  installed before their next build (the same prerequisite as the `net10` retarget in this release).

## [0.4.1-beta] - 2026-07-17

### Fixed
- **Config is now saved atomically and never silently reset to empty.** A crash or power loss
  mid-save could leave `config.json` truncated; the next launch would fail to parse it, fall back
  to defaults, and then overwrite the file with an empty config — losing every meter and its serial
  pinning. Saves now write to a temp file and atomically rename it into place, and an unreadable
  config is preserved as `config.json.bak` instead of being discarded. (`AtomicFile` in W2.Core.)
- **Disconnecting a meter no longer leaves it stuck "transmitting."** A reading already queued to
  the UI thread when you disconnected could run afterward, re-showing live data, sticking the TX
  indicator on, and even stealing focus for the disconnected meter. Queued reader callbacks are now
  dropped once disconnect begins. (`MeterService`.)
- **Reset Peak now resets the meter you selected in Setup**, not whichever meter currently has
  auto-focus. (`SetupViewModel`.)
- **A failed in-app update no longer looks like it succeeded.** If the file swap failed (file locked
  or not writable), the helper used to relaunch the old exe while the UI claimed the update applied.
  The swap is now checked; on failure the app relaunches and, on next start, tells you the update
  didn't apply and you're still on the old version. (`UpdateApplyScript` in W2.Core.)
- **Linux: one bad `/dev/serial/by-id` symlink no longer drops the other cables.** A dangling entry
  (common mid-replug) previously aborted the whole port scan, so a second W2 couldn't follow its
  cable across a renumber. Each entry is now handled independently. (`PortIdentity`.)

### Changed
- **One-window-per-meter mode: closing any meter window closes them all.** The per-meter windows are
  one app view, so closing one now closes the rest (and exits) instead of making you close each one.
  Removing a single meter or switching window modes still closes just the affected window.

## [0.4.0-beta] - 2026-07-17

### Fixed
- **In-app update no longer crashes on launch.** Release builds are now a TRUE single-file exe
  with the native libraries (Skia/HarfBuzz/ANGLE) bundled *inside* it. Previously the publish
  shipped those DLLs loose beside the exe; since the in-app updater replaces only the exe, an
  updated build landed with no native dependencies and crashed on startup. The
  `IncludeNativeLibrariesForSelfExtract` setting is now baked into the project file so the
  documented publish command can't miss it. Updating from an earlier build repairs itself — the
  new self-contained exe no longer needs the loose DLLs. (Same failure that broke LP-100A v0.9.4.)

## [0.3.8-beta] - 2026-07-12

### Added
- **SWR alarm.** The SWR bar is now colored so it "goes red where your alarm trips," and flashes
  red on a live alarm. A new Setup **SWR ALARM** group sets the trip point (▼/▲, 1.1–5.0), resets
  a latched alarm, and toggles latching — driving the W2's rear-panel keyline-disconnect relay.
  The trip point is read from the meter on connect so the bar anchors correctly.

### Changed
- **SWR bar matches the power bar** — same height and square corners, so they read as a pair.

## [0.3.7-beta] - 2026-07-12

### Fixed
- **Alternating S1/S2 transmit no longer ignores the second sampler.** When you keyed one
  sampler, stopped, then keyed the other, the readout could stick on the first for several
  seconds (often the whole over) because the W2 stops hunting to the sampler that went quiet. It
  now follows the RF over to the active sampler promptly — while still holding through stray RF
  the meter hunts to on the idle sampler during a single over.

### Changed
- **Window mode is now a simple toggle.** Replaced "open a dedicated window for the selected
  meter" with a **"One window per meter"** checkbox in Setup (shown with 2+ meters): off is one
  auto-focus window, on gives a dedicated window per meter (no separate focus window). Switching
  opens the new layout before closing the old, so you can't lose your last window.

## [0.3.6-beta] - 2026-07-12

### Added
- **Dedicated per-meter windows.** With more than one W2 connected, open a dedicated window for
  any meter (**Setup → "Open a dedicated window for the selected meter"**) — each pinned to its
  meter and named in the title bar. A multi-meter station can lay them out side-by-side instead
  of squinting at one auto-focus readout; close the auto-focus window and keep just the dedicated
  ones if you like. Your window layout (which are open, and where) is remembered between sessions.
  Single-meter use is unchanged.

## [0.3.5-beta] - 2026-07-12

### Fixed
- **Readout no longer flickers to a sampler catching stray RF.** In Search mode the W2 hops
  between its two samplers; if the idle one caught a little stray RF, the display jumped between
  the real over and the stray reading. It now **locks to the sampler carrying the over and
  ignores the other** until that over ends. Applied per meter, so it holds independently across
  multiple W2s. (With several W2s transmitting at once, the main display still shows the strongest
  one — pin another in Setup to watch it.)

### Changed
- **Check-for-updates and Update-now merged into one button.** The separate "Update now" only
  appeared after a check and was easy to miss. A single button now checks, then becomes
  "Update now" once an update is found.
- **Removed the bundled desktop-shortcut helpers.** They added launcher warnings/prompts on both
  platforms. The README now explains how to make a shortcut with your OS's own tools instead.

## [0.3.4-beta] - 2026-07-07

### Added
- **Desktop-shortcut helper in each download**, brought over from the PowerShell version.
  - **Windows:** run `Create Desktop Shortcut.vbs` to drop a "W2 Monitor" shortcut on the
    Desktop (points at `W2Monitor.exe`, uses its embedded icon).
  - **Linux/Pi:** run `./install-desktop-shortcut.sh` to add a "W2 Monitor" entry to your
    applications menu (and Desktop), with a bundled icon and a `dialout` reminder.

## [0.3.3-beta] - 2026-07-07

First fix from the Raspberry Pi / CM5 serial shakeout (see `HANDOFF-PI.md`). Validated on a
live CM5 against a real FTDI cable, including a forced USB drop/renumber.

### Fixed
- **Auto-reconnect after a USB drop or renumber (Linux/Pi).** A W2 that dropped off the bus
  (loose cable, power blip, or the FTDI re-enumerating to a new `/dev/ttyUSB*`) was lost until
  the app restarted: the reader spun forever on the dead handle, leaking a `"(deleted)"` fd and
  freezing the readout on its last value. The reader now **detects the loss** (a hard port error,
  or a run of empty poll cycles via the new `LinkHealth`), **closes the port** so no fd leaks, and
  **reconnects** — re-resolving `/dev/serial/by-id` every attempt so it follows the cable to
  whatever port it now maps to. `IsConnected` is restored on reconnect so the W2 controls come
  back live.
- **Never wedge on a surprise-removed FTDI.** On Linux `SerialPort.Open()`/`Close()` can block
  forever when the device vanished mid-call — this is what left the old reader stuck. Both are now
  watchdog-bounded, so a dropped device can't stall the reconnect loop or `Stop()`/shutdown.

### Added
- `LinkHealth` (in `W2.Core`, unit-tested) — decides when a silent link is dead vs. a single
  skipped reply, keeping reconnect decisions out of the serial plumbing.

## [0.3.2-beta] - 2026-07-05

### Added
- **Setup meter list shows the cable's serial** after the COM port, e.g. `W2 #1 · COM4
  (A10KMB4VA)` — like the earlier PowerShell version. On Linux the long `/dev/serial/by-id`
  name is shortened to the embedded serial with a leading `…` (e.g. `…A10KMB4VA`).

## [0.3.1-beta] - 2026-07-05

UI polish and out-of-the-box usability from first dogfooding.

### Changed
- **Main window header:** the "W2 MONITOR" title is now amber (matching the Setup control
  lamps) with the glow removed; the redundant "· Connected on COMx" text is gone (that lives
  in Setup); the connection dot moved to sit immediately left of the Setup button.
- **Line 2** now leads with the focused meter's name when more than one meter is connected
  (which W2 is in use), and shows Disconnected / No meters there when nothing is live.
- **Accent color:** line 2 and the forward-power bar now use the theme accent — the same blue
  as the Setup meter-list selection — and track the OS accent.
- **Auto-select the first meter** in Setup on load, so the W2 controls are usable immediately
  without a manual pick (most users have a single W2, which also auto-connects on launch).

## [0.3.0-beta] - 2026-07-05

Promoted to **beta** for dogfooding. Same feature set as 0.2.0-alpha, now the first public
"Latest" release so the in-app updater can see it. Windows validated on two live W2s; Linux
and Raspberry Pi builds run but are still being tested on real hardware.

### Changed
- Version → 0.3.0-beta; README rewritten for end users (install/features), superseding the old
  scaffold notes.
- This is now the sole W2 Monitor line — the PowerShell version is retired/archived.

## [0.2.0-alpha] - 2026-07-05

First testable build of the cross-platform port. Windows validated against two live W2s;
Linux/Raspberry-Pi builds compile and publish but are not yet hardware-tested.

### Added
- **Live multi-meter readout** — forward power, SWR (green/amber/red), reflected, return
  loss, and a custom stacked bar with a cyan **peak-hold marker**. The display auto-focuses
  whichever meter is transmitting (highest over-peak; a manual pick in Setup pins it).
- **Full W2 control** (Setup, acts on the selected meter): Auto Sensor, Auto Range,
  Avg/PEP, Manual Sensor, Manual Range, LEDs — with live lamps (auto/LEDs from the meter's
  status, Avg-PEP and Search echo-tracked, probed on connect).
- **TX-timeout timer** — solid yellow for the last 30 s, flashing red at/after the timeout
  (silent).
- **Setup**: meter list (add/remove, assign port, connect), **Detect** (port probing behind
  a "may key a radio" confirm), display toggles, and an in-app update checker.
- **FTDI/serial pinning** — follows each cable by its chip serial (Windows) or
  `/dev/serial/by-id` (Linux) across port renumbering.
- **`--sim`** flag drives the UI from synthetic meters (no hardware needed).

### Verified on hardware (Windows)
- Two W2s (renumbered ports transparently re-pinned by serial), full decode pipeline, value
  scaling under a real carrier, and the control-command echo path. Real captured frames are
  locked in as regression tests.

### Known gaps
- Linux/Raspberry-Pi runtime not yet tested on real hardware.
- The in-app updater's GitHub repo slug is a placeholder pending the public repo.
