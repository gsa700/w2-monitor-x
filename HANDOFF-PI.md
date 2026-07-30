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
dotnet build                                   # needs the .NET 10 SDK
dotnet test                                    # 168 tests, all pure Core logic — should pass on ARM
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
  <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
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
- **Release recipe** — the order matters; see the two notes under it:
  1. `dotnet test` — all green before anything else.
  2. Bump `<Version>` in `src/W2.App/W2.App.csproj`, date the `CHANGELOG.md` section, and **commit
     that before publishing.**
  3. Publish 3 RIDs self-contained single-file (`win-x64`, `linux-x64`, `linux-arm64`), then zip each
     as `W2Monitor-<rid>.zip` — those exact asset names are what the updater matches on.
  4. **Smoke-test a published binary before uploading it.** Launch it with `--sim` and confirm it's
     still alive several seconds later.
     **On Windows, do not expect `APPDATA` to isolate it** — .NET resolves
     `SpecialFolder.ApplicationData` through the known-folder API, not the environment variable, so a
     redirected `APPDATA` is ignored and the smoke test reads the real `config.json`. What actually
     protects it is ending the run with a force kill (`Stop-Process -Force`), so the app never
     reaches its save-on-exit path; copy `config.json` aside first if you want certainty. On Linux,
     redirecting `HOME`/`XDG_CONFIG_HOME` does work.
  5. `git tag -a vX.Y.Z-beta` → `git push origin main --follow-tags`.
  6. `gh release create vX.Y.Z-beta <zips> --title … --latest`. **Use `--latest`, NOT
     `--prerelease`** — the in-app updater queries `/releases/latest`, which skips pre-releases.
  7. Verify what the updater will actually see, rather than assuming:
     `curl -s https://api.github.com/repos/gsa700/w2-monitor-x/releases/latest` must report the new
     tag and all three asset names.

  **Why the bump is committed first (step 2 before step 3):** the informational version embeds the
  current commit sha, so publishing before committing stamps the binaries with the commit *preceding*
  the bump — a sha that is not the release tag, which misleads anyone tracing a field report back to
  source. Releases through v0.5.0-beta carry that skew; v0.5.1-beta onward don't.

  **Why step 4 exists:** single-file bundling only applies on publish with a RID — `dotnet build`/`run`
  ignore it — so a whole class of breakage first appears in the published artifact and in nothing you
  ran while developing. v0.4.0-beta shipped a single-file build that crashed on launch because the
  native libs weren't bundled, and the in-app update path was what surfaced it, for users.
- `gh` is authed as **gsa700**; repo is **gsa700/w2-monitor-x**; the updater slug matches.
- Commit trailer: `Co-Authored-By: Claude <model> <noreply@anthropic.com>`, naming the model you're
  actually running as (e.g. `Claude Opus 5`) — history has 4.8 and 5 as the models changed.
- Confirm outward-facing actions (public releases, repo changes) with David first.
- **Dogfooding feedback → `BACKLOG.md`**, batched into releases.
- Keep new Core logic **pure and unit-tested** (`tests/W2.Core.Tests`, xUnit) — that's how the
  serial/protocol code earned its confidence and how you'll earn it for the Linux paths.

## Current state

*Refreshed 2026-07-30 — the rest of this doc is the 2026-07-06 snapshot described in the banner up top.*

- Branch `main`, in sync with `origin/main`; latest release **v0.6.0-beta**, tagged at the head commit.
  The retired PowerShell app is at `github.com/gsa700/w2-monitor` (archived).
- **Landed since this doc was written:** the SWR alarm and its bar coloring (v0.3.8-beta), single-file
  native-lib bundling (v0.4.0-beta — see the smoke-test step in the recipe for why that matters),
  per-meter windows and the config-durability batch (v0.4.1-beta), the **.NET 8 → 10 retarget** plus a
  D-Bus CVE pin (v0.5.0-beta), the last of the bug-hunt fixes (v0.5.1-beta), and **Avalonia 12.1.1, a
  self-installer and a tabbed Setup** (v0.6.0-beta). `CHANGELOG.md` has the detail; `BACKLOG.md` is the
  live list of what's open.
- The .NET 10 retarget means **this box needs the .NET 10 SDK** before `dotnet build` will work here —
  the `global.json` pin is deliberate, so don't downgrade it to whatever SDK happens to be installed.
- Test count is **168**, not the 78 quoted in the build section when this doc was written.

### The current CM5 job: shake down the installer's Linux paths

The serial mission at the top of this doc is long done. What actually needs a Pi now is the
**self-install added in v0.6.0-beta**, because none of its filesystem work has ever run on Linux — it
compiles, cross-publishes, and its pure logic (`InstallLayout`, `InstallCommandLine`, `DesktopEntry`)
is unit-tested, but that's all. Read *Self-install (Windows and Linux)* in `CLAUDE.md` first.

What to exercise, in rough order of how badly it fails if it's wrong:

1. **`--uninstall` is where a mistake is unrecoverable.** `Unregister` must remove the `.desktop`
   entry, the hicolor icon and the `~/.local/bin/w2-monitor` symlink **one file at a time** — those
   live in shared directories, and deleting one as a directory takes every user binary with it. Only
   the app's own install directory may be removed wholesale, and only when the copy is `Installed`.
   Check `~/.local/bin` still holds everything else afterwards.
2. **The executable bit.** A copied binary arrives without it; `MakeExecutable` sets it. Without it,
   the menu entry silently does nothing — no error anywhere.
3. **The `.desktop` entry** actually appearing in the menu, and `update-desktop-database` being absent
   not breaking anything.
4. **The icon** reaching `~/.local/share/icons/hicolor/256x256/apps/w2-monitor.png`, and the dock
   matching the running window to it via `StartupWMClass=W2Monitor`.
5. **The `sh` uninstall trampoline** — it waits on the pid, then deletes. Confirm it removes itself.

Redirecting `HOME`/`XDG_CONFIG_HOME` genuinely does isolate config on Linux (unlike `APPDATA` on
Windows), so a throwaway run is easy to arrange here.

Also unverified on this box: **Avalonia 12 rendering**. v0.6.0-beta is the first release on it, and the
Pi is the one platform where the D-Bus layer is actually used.
- You're in **bash on Linux**, not PowerShell. `tools/Capture-W2.ps1` needs `pwsh` (likely
  absent) — prefer the harness above or plain shell tools.
- Native on the Pi you *can* launch the GUI and look at it. A Windows-side session can launch it and
  confirm the process survives, but not see what it renders — so render/scale and layout calls are
  still yours to make (loop David in for visual ones).
