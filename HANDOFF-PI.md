# Handoff — continuing W2 Monitor on the Raspberry Pi / CM5

You are a fresh Claude Code session running **on the Pi**. You don't have the prior
conversation, but you have this repo. This doc is your on-ramp. Written 2026-07-06 by the
Windows-side session that built the port.

**Read first:** `README.md`, `CHANGELOG.md`, `BACKLOG.md`, then this file.

> **Status — RESOLVED (2026-07-07):** the serial shakeout below is done. Auto-reconnect /
> follow-the-cable landed in **v0.3.3-beta**, and the app is now validated on real hardware
> across Windows, Raspberry Pi CM5 (arm64), and Fedora (x64) — full test suite passing on each,
> identical behavior. This doc is kept as a reference map of the serial subsystem and the CM5
> gotchas for any future Linux serial work; the "mission" below is complete.

---

## The mission

**Validate and fix the serial subsystem on the CM5.** Everything OS-specific here has *only*
ever run on Windows — the Linux/ARM serial paths are unproven on real hardware, and David's
read is *"the serial code is sort of a mess on the CM5."* Treat the Linux serial paths as
suspect; treat the Windows-validated logic (below) as trustworthy.

## Get building on the Pi

```sh
git clone https://github.com/gsa700/w2-monitor-x && cd w2-monitor-x
dotnet build                                   # needs the .NET 8 SDK
dotnet test                                    # 78 tests, all pure Core logic — should pass on ARM
dotnet run --project src/W2.App                # the app (needs a desktop/DISPLAY)
dotnet run --project src/W2.App -- --sim       # UI from a synthetic meter, no hardware
```

Serial prerequisites on Linux: your user must be in the **`dialout`** group
(`sudo usermod -aG dialout $USER`, then re-login). Self-contained builds bundle
`libSystem.IO.Ports.Native.so`; a source `dotnet run` uses the SDK's.

## Serial subsystem map (where the code lives)

- **`src/W2.Core/`** (no UI, unit-tested):
  - `SerialReader.cs` — the real port: 9600 8N1, DTR+RTS asserted, query/response loop that
    polls `F`/`R`/`S`/`I` each cycle (~80 ms), plus a command queue (`O 0 1 2 3 N L Y`), the
    `N`/`Y` echo capture, and the connect-time double-toggle probe for Avg-PEP/Search.
  - `W2FrameParser.cs` / `W2Wire.cs` / `W2Reading.cs` — decode/encode. F/R = `[FfRr](\d+)D(\d)`
    → digits/10^n; S = `[Ss](\d+)` → /100; I = byte-map (range/type/LEDs/active sampler/alarm).
  - `StreamFramer.cs` (`ReplyFramer`) — splits `;`-terminated replies.
  - `IReadingSource.cs`, `W2SimReader.cs`, `W2Probe.cs` (Detect), `SerialErrors.cs`,
    `SerialDisplay.cs`.
- **`src/W2.App/Services/`**:
  - `PortIdentity.cs` — cable pinning. **Windows** = FTDI chip serial via WMI. **Linux** =
    `PopulateLinux()` maps each `/dev/serial/by-id/*` symlink → its `/dev/tty*` target, using
    the by-id name as the stable id. **This Linux path has never run on hardware.**
  - `MeterService.cs` (one meter: reader + TX/peak/derived state), `MeterManager.cs` (N meters
    + auto-focus).

## Likely CM5 problem areas — hypotheses to verify (NOT confirmed)

These are educated guesses from the Windows side; confirm each on the CM5 before acting.

1. **Port list clutter.** `SerialPort.GetPortNames()` on a Pi may surface on-board UARTs
   (`/dev/ttyAMA*`, `/dev/ttyS*`) alongside the FTDI `/dev/ttyUSB*`. That clutters the Setup
   port dropdown and — worse — makes **Detect** probe non-W2 UARTs. Consider filtering Linux
   enumeration to `ttyUSB*`/`ttyACM*` (and/or preferring `/dev/serial/by-id/*` entries).
   Check: `ls /dev/tty* ; ls -l /dev/serial/by-id/`.
2. **Detect on the Pi.** `W2Probe.Detect` opens *every* candidate port, asserts DTR/RTS, sends
   `V`. Opening/poking on-board UARTs is wasteful and may hang or misbehave. Gate Detect to USB
   serial on Linux.
3. **by-id pinning.** Verify `/dev/serial/by-id/` exists (some minimal images lack the udev
   rules) and that `File.ResolveLinkTarget(link, true)` resolves to the real `ttyUSB`. If the
   dir is absent, pinning silently no-ops and falls back to the saved port name — meters would
   still work but not follow a replug/renumber.
4. **System.IO.Ports on ARM/Linux quirks.** DTR/RTS via termios, `BytesToRead`, and
   `ReadTimeout` semantics differ from Windows. The `Query()` write-then-poll loop and the
   200 ms timeouts may need tuning for Pi scheduling/latency. Watch for chronic per-field
   dropouts (nulls) or a laggy readout.
5. **Serial identity string.** On Linux `Meter.Serial` is the long by-id *name*; config stores
   it as `"Serial"`, and `SerialDisplay.Shorten` renders it as `…<serial>`. Sanity-check that.
6. **Permissions.** `SerialErrors.Describe(..., isLinux: true)` should surface the dialout hint
   on `UnauthorizedAccessException` — confirm it fires when not in the group.

## Fastest way to see what the Core actually does (headless serial harness)

Drop this in `/tmp/probe/` and `dotnet run --project /tmp/probe -- /dev/ttyUSB0`. It runs the
real `SerialReader` and prints decoded readings + status — the single most useful debugging
tool (this is how the Windows serial paths were validated).

`/tmp/probe/probe.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
  <OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
</PropertyGroup><ItemGroup>
  <ProjectReference Include="/absolute/path/to/w2-monitor-x/src/W2.Core/W2.Core.csproj" />
</ItemGroup></Project>
```
`/tmp/probe/Program.cs`:
```csharp
using W2.Core;
var port = args.Length > 0 ? args[0] : "/dev/ttyUSB0";
Console.WriteLine("ports: " + string.Join(", ", SerialReader.GetPortNames()));
var r = new SerialReader();
r.StatusChanged += (m, e) => Console.WriteLine($"[status{(e ? " ERR" : "")}] {m}");
var n = 0;
r.ReadingReceived += x => { if (n++ % 8 == 0)
    Console.WriteLine($"fwd={x.ForwardPowerW} swr={x.Swr} status={x.HasStatus} " +
                      $"sensor={x.ActiveSampler} type={x.TypeName} range={x.RangeName} pep={x.Pep} search={x.Search}"); };
r.Start(port); Thread.Sleep(8000); r.Stop();
```
Also just inspect the environment directly: `ls -l /dev/serial/by-id/`, `ls /dev/tty*`,
`groups`, `dmesg | grep -i ftdi`.

## What's validated (trust it) vs. untested (suspect it)

- **Validated on Windows hardware, locked as regression tests:** the whole decode pipeline
  (idle + a real ~15 W carrier), FTDI serial pinning across COM renumber, multi-meter +
  auto-focus, the W2 control layer (echoes + connect probe), and the in-app updater end-to-end.
  Don't "fix" the protocol/parser without a captured frame proving a real discrepancy — add it
  as a test if so.
- **Untested anywhere but this task:** Linux port enumeration, `/dev/serial/by-id` pinning,
  dialout error path, ARM serial timing, and Skia render/scale on the Pi. This is your turf.

## How we ship (project conventions)

- **Version** lives in `src/W2.App/W2.App.csproj` (`<Version>`). Bump it + add a `CHANGELOG.md`
  entry per release. Versions < 1.0 are `-beta` ("in use, not broadly field-tested").
- **Release recipe:** `dotnet test` → publish 3 RIDs self-contained single-file
  (`win-x64`, `linux-x64`, `linux-arm64`) → zip each as `W2Monitor-<rid>.zip` → commit →
  `git tag -a vX.Y.Z-beta` → `git push origin main --follow-tags` →
  `gh release create vX.Y.Z-beta <zips> --title … --latest`. **Use `--latest`, NOT
  `--prerelease`** — the in-app updater queries `/releases/latest`, which skips pre-releases.
- `gh` is authed as **gsa700**; repo is **gsa700/w2-monitor-x**; the updater slug matches.
- Commit trailer: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Confirm outward-facing actions (public releases, repo changes) with David first.
- **Dogfooding feedback → `BACKLOG.md`**, batched into releases.
- Keep new Core logic **pure and unit-tested** (`tests/W2.Core.Tests`, xUnit) — that's how the
  serial/protocol code earned its confidence and how you'll earn it for the Linux paths.

## Current state

- Branch `main`; latest is the README-screenshots commit; latest release **v0.3.2-beta**
  (`main` is ~1 doc commit ahead of that tag). The retired PowerShell app is at
  `github.com/gsa700/w2-monitor` (archived).
- You're in **bash on Linux**, not PowerShell. `tools/Capture-W2.ps1` needs `pwsh` (likely
  absent) — prefer the harness above or plain shell tools.
- Native on the Pi you *can* launch the GUI and see it; the Windows session could not — so
  render/scale observations are yours to make (loop David in for visual calls).
