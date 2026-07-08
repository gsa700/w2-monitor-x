# Backlog

Dogfooding feedback and small improvements, batched into releases.

## Open

- **Reconnect status wording.** During a replug the transient "Permission denied … add to
  dialout" message can flash for ~1 s before the new port resolves (the device is mid-re-enum,
  not a real permissions problem). Consider suppressing the dialout hint while actively
  reconnecting so it doesn't alarm users. (Seen during the CM5 forced-drop test, v0.3.3-beta.)

## Done

- **Auto-reconnect / follow-the-cable on Linux after a USB drop or renumber** (v0.3.3-beta) —
  the reader now detects a lost link (`LinkHealth`), releases the fd, and reconnects by
  re-resolving `/dev/serial/by-id`; `Open`/`Close` are watchdog-bounded so a surprise-removed
  FTDI can't wedge the thread. Verified on a live CM5 with a forced deauthorize/re-authorize
  that renumbered ttyUSB3→ttyUSB2. (`SerialReader`, `LinkHealth`, `MeterService.ResolveCurrentPort`.)

- **Setup meter list shows the cable serial after the COM port** (v0.3.2-beta) — e.g.
  `W2 #1 · COM4 (A10KMB4VA)`. On Linux the long `/dev/serial/by-id` name is shortened to the
  embedded serial with a leading `…` (e.g. `…A10KMB4VA`) to stay about the Windows length.
  (`SerialDisplay.Shorten` in W2.Core; used by `MeterRow`.)
