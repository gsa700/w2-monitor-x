# Changelog

Cross-platform **W2 Monitor** (.NET 8 + Avalonia). Companion to the original PowerShell
app; this is the Windows/Linux/Raspberry-Pi rewrite.

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
