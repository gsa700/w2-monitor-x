# Changelog

Cross-platform **W2 Monitor** (.NET 8 + Avalonia). Companion to the original PowerShell
app; this is the Windows/Linux/Raspberry-Pi rewrite.

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
