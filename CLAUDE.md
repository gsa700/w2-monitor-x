# W2 Monitor (w2-monitor-x)

Cross-platform desktop monitor for **Elecraft W2** RF power/SWR meters — multi-meter,
full W2 control, TX-timeout timer, SWR alarm. **.NET 10 + Avalonia 12.1.1**, MVVM.
Runs on Windows, Linux, and Raspberry Pi (arm64). GPLv3. By David Erickson (AB0R).

This is the cross-platform successor to the retired PowerShell `w2-monitor`. It is the
sole, ongoing W2 Monitor line — all W2 work happens here.

## Build / run / test

```sh
dotnet build                                   # needs the .NET 10 SDK (pinned in global.json)
dotnet run --project src/W2.App                # run the app (needs a desktop/DISPLAY)
dotnet run --project src/W2.App -- --sim       # no hardware: drive UI from synthetic W2s
dotnet run --project src/W2.App -- --setup     # open Setup on launch (debug)
dotnet test                                    # xUnit suite — all pure W2.Core logic (168 tests)
```

Runtime switches the app itself understands: `--sim`, `--setup`, and the install pair `--install` /
`--uninstall` (both take `--quiet` for an unattended run). See *Self-install* below.

Solution: `W2Monitor.sln`. Output assembly is `W2Monitor` (`W2Monitor.exe` on Windows).

Publish a self-contained build (per platform):

```sh
dotnet publish src/W2.App -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -o publish/win-x64
# swap -r for linux-x64 or linux-arm64 (Raspberry Pi)
```

## Layout

```
src/
  W2.Core/   # NO UI. Serial + protocol + pure logic — this is where the tests live.
             #   Serial:  SerialReader (supervisor loop + watchdog), StreamFramer, W2FrameParser,
             #            W2Wire (build wire strings), W2Reading, W2SimReader, W2Probe (Detect),
             #            SerialErrors, SerialDisplay, LinkHealth, IReadingSource
             #   Logic:   FocusPolicy, SensorLock (Search-mode steadying), TxTimer
             #   Plumbing: AtomicFile (durable config writes), UpdateApplyScript
             #   Install: InstallLayout (Installed/Portable/Loose), InstallCommandLine, DesktopEntry
  W2.App/    # Avalonia MVVM
             #   Services/  MeterManager (owns N meters), MeterService (per-meter model),
             #              PortIdentity (cable pinning), UpdateService, InstallService, AppConfig
             #   ViewModels/ MainWindow, Setup, ViewModelBase
             #   Views/     MainWindow, SetupWindow (tabbed), ConfirmWindow
             #   Controls/  PowerSwrBar (stacked fwd bar + cyan peak-hold marker)
tests/W2.Core.Tests/   # xUnit — Core logic only (no UI). Keep new logic testable here.
```

**Design rule:** all non-UI logic lives in `W2.Core` and is unit-tested; `W2.App` is the
Avalonia shell. Put new parsing/decision logic in Core with tests, not in view-models.

## W2 serial protocol (validated on real hardware)

- **9600 8N1, DTR+RTS asserted, query/response** (differs from LP-100A's single-`P` stream).
- Each cycle polls **F / R / S / I**; replies are `;`-terminated.
  - `F/R` (fwd/refl power): `[FfRr](\d+)D(\d)` → digits / 10^n
  - `S` (SWR): `[Ss](\d+)` → digits / 100
  - `I` string: byte map for range/full-scale, auto, type, sampler LEDs, active sampler, alarm
- Control (echo-based, acts on selected meter): Auto Sensor `Y`, Auto Range `0`, Avg/PEP `N`,
  Manual Sensor `O`, Manual Range `1/2/3`, LEDs `L`; confirm via `N`/`Y` echo.
- **Detect** (`W2Probe`) sends `V` and matches `^[Vv]\d`. It may key a radio, so it is gated
  behind a ConfirmWindow and disabled in `--sim`.
- Regression tests pin real captured frames (e.g. `F01553D2`→15.53 W, `S0108`→1.08).

## Cable identity

Each W2 is followed by its USB chip serial: FTDI serial pinning on **Windows** (`System.Management`),
`/dev/serial/by-id/*` on **Linux/Pi**. A replug/renumber must not lose a meter. On Linux the
`SerialReader` supervisor auto-reconnects and re-pins by serial across USB drops.

## Config & updater

- App config: `%AppData%/W2Monitor/config.json` (Windows). Holds `Meters[]` (port + chip serial).
- In-app updater (`UpdateService`) targets GitHub `gsa700/w2-monitor-x`, checks `/releases/latest`.
  **`/releases/latest` excludes pre-releases**, so an `-beta` marked as a full "Latest" release
  is what the updater will see; alpha/pre-release tags won't surface to users.

## Hardware & workflow notes

- **Both W2s are attached to this Windows box** (verified 2026-07-19) — live hardware testing
  works here. `--sim` is still the way to work without touching the rig. On-air dogfooding of
  releases happens at the station. *(An earlier version of this note said this box had no W2
  attached and that both lived on HAMSTATION — no longer true here.)*
- **Identify every USB adapter by its chip serial, never by COM port.** Ports renumber; all of
  them changed across a clean Windows 11 reinstall on 2026-07-19. Every FT232R here reports
  stock EEPROM (`USB Serial Converter`, no programmed product name), so nothing in Windows
  tells them apart by inspection — this table is the only mapping:

  | Chip serial | Device |
  |---|---|
  | `A10KMB4VA` | Elecraft W2 #1 |
  | `AG0JFX7UA` | Elecraft W2 #2 |
  | `ABSCDI99A` | TelePost LP-100A |
  | `AD0JLU2FA` | Kenwood TM-V71A |
  | FT2232 dual-channel (`PID_6010`, two ports) | Elecraft K4D |

  **Never run Detect to work out which adapter is which** — it sends `V` and may key a radio,
  and two of these are transmitters. Read the table, or ask.
- Cross-platform validated on real hardware: **Windows, Pi CM5 (linux-arm64), Fedora (linux-x64)**.
- A Pi-side Claude session has worked this repo too (`HANDOFF-PI.md`); the two boxes sync via git
  (`main`, two-way pull/push). Keep `main` clean and rebased-friendly.

## Self-install (Windows and Linux)

Ported from LP-100A Monitor, which established the pattern — read its `CLAUDE.md` section of the same
name for the full rationale. No Inno/WiX/MSI and no new toolchain: the app installs *itself* via
`--install` / `--uninstall`, with the pure decisions in `W2.Core` (`InstallLayout`,
`InstallCommandLine`, `DesktopEntry` — all unit-tested) and the side effects in
`W2.App/Services/InstallService.cs`.

- **Per-user is a constraint, not a preference.** `UpdateService.ApplyAndRestart` replaces the running
  executable in place, which needs no elevation under `%LOCALAPPDATA%\Programs` and would need it on
  every update under `Program Files`. A machine-wide installer would quietly break the updater. Don't
  "fix" the install location without re-reading that method.
- **Location is the mode.** `InstallLayout.Detect` returns Installed / Portable / Loose from the
  executable's directory plus a `portable.txt` marker. Nothing is written that could disagree with
  where the file actually is, and the marker beats everything, including the install directory.
- **Pre-installer copies are adopted where they stand.** `LegacyFolders` covers `W2Monitor` and the
  three RID-suffixed folder names Explorer creates from the release zips — this station's own install
  is `%LOCALAPPDATA%\Programs\W2Monitor-win-x64`, and without adoption the first run would install a
  second copy and orphan the live one.
- **Uninstall only deletes a directory the app owns.** `Uninstall` removes `ExeDirectory` *only* when
  `Mode == Installed`; a Loose copy's folder is not ours, and someone who extracted the exe straight
  into Downloads would otherwise lose Downloads. **This is a divergence from LP-100A, which deletes it
  unconditionally — worth porting back there.** Shared directories (`~/.local/bin`, the icon theme,
  `~/.local/share/applications`) are always removed one file at a time, never as a directory.
- **Settings are named, not swept.** `DataFilesToRemove` lists `config.json` and its `.bak`; the data
  directory itself is never removed, so anything a later version puts there survives an older
  uninstall. The prompt defaults to keeping them — they hold the meter list and each cable's chip
  serial. A quiet uninstall (what the installed-apps entry's own button runs) always keeps them,
  because Windows gives the user no way to answer a dialog it didn't expect.
- **Registry goes through `reg.exe`, deliberately** — the registry APIs need `net10.0-windows`, and the
  plain `net10.0` TFM is what lets one target cross-publish Linux and Pi. The entry is written,
  verified, and rewritten once if the check fails: on LP-100A a spawn silently didn't take, leaving
  the app installed but absent from Installed apps with no error anywhere.
- **The Linux icon is the 256px frame lifted verbatim from `app.ico`**, embedded as a plain
  `EmbeddedResource` because `--install` runs before Avalonia is initialised, so the asset loader
  isn't available. Regenerate it from `app.ico` rather than editing the two separately.

> **Linux is unverified on hardware**, exactly as in LP-100A. It compiles, cross-publishes, and its
> pure logic is unit-tested, but no part of the filesystem work — icon write, `.desktop` entry,
> symlink, `chmod`, the `sh` uninstall trampoline — has run on a real Linux box. The CM5 is the place
> to settle it. Windows is verified end to end, both uninstall paths included.

## Release workflow

`gh` is installed and authed as `gsa700`. A release = git tag + three self-contained zips
(`W2Monitor-win-x64.zip`, `-linux-x64.zip`, `-linux-arm64.zip`) attached to a GitHub release
(asset names must match what the updater expects). Version scheme mirrors the PS app:
`<1.0` = `-beta` (in use, not broadly field-tested); publish as a full "Latest" release so the
updater sees it. Update `CHANGELOG.md` for every release.

Two ordering traps, both spelled out in the step-by-step recipe in `HANDOFF-PI.md`: **commit the
version bump before publishing** (binaries embed the commit sha, so publishing first stamps them with
a sha that isn't the release tag), and **smoke-test a published single-file binary before uploading**
(single-file bundling only applies on publish with a RID, so `build`/`run` can't surface a break in it).
