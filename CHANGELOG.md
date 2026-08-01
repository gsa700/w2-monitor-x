# Changelog

Cross-platform **W2 Monitor** (.NET 10 + Avalonia). Companion to the original PowerShell
app; this is the Windows/Linux/Raspberry-Pi rewrite.

## [Unreleased]

### Fixed
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
