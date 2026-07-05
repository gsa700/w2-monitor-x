# Backlog

Dogfooding feedback and small improvements, batched into releases.

## Open

- **Setup meter list: show the FTDI serial after the COM port.** The row currently reads
  `W2 #1 · COM4`; append the cable's chip serial like the earlier PowerShell version did, e.g.
  `W2 #1 · COM4 (A10KMB4VA)`. (Source: `MeterRow.Text` in `SetupViewModel.cs`, from `Meter.Serial`.)
  Note for implementation: on Linux the "serial" is the longer `/dev/serial/by-id` name — may want
  to shorten or show only on Windows.
