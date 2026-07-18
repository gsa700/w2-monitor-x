# W2 Monitor (w2-monitor-x)

Cross-platform desktop monitor for **Elecraft W2** RF power/SWR meters — multi-meter,
full W2 control, TX-timeout timer, SWR alarm. **.NET 8 + Avalonia 11.2.1**, MVVM.
Runs on Windows, Linux, and Raspberry Pi (arm64). GPLv3. By David Erickson (AB0R).

This is the cross-platform successor to the retired PowerShell `w2-monitor`. It is the
sole, ongoing W2 Monitor line — all W2 work happens here.

## Build / run / test

```sh
dotnet build                                   # needs the .NET 8 SDK
dotnet run --project src/W2.App                # run the app (needs a desktop/DISPLAY)
dotnet run --project src/W2.App -- --sim       # no hardware: drive UI from synthetic W2s
dotnet run --project src/W2.App -- --setup     # open Setup on launch (debug)
dotnet test                                    # xUnit suite — all pure W2.Core logic
```

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
             #   SerialReader (supervisor loop + watchdog), StreamFramer, W2FrameParser,
             #   W2Wire (build wire strings), W2Reading, W2SimReader, W2Probe (Detect),
             #   FocusPolicy, SensorLock (Search-mode steadying), TxTimer, LinkHealth,
             #   SerialErrors, SerialDisplay, IReadingSource
  W2.App/    # Avalonia MVVM
             #   Services/  MeterManager (owns N meters), MeterService (per-meter model),
             #              PortIdentity (cable pinning), UpdateService, AppConfig
             #   ViewModels/ MainWindow, Setup, ViewModelBase
             #   Views/     MainWindow, SetupWindow, ConfirmWindow
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

- **The dev/build machine (this Windows box) usually has NO W2 attached.** The two physical W2s
  live on **HAMSTATION** (the shack PC), which downloads releases and does on-air dogfooding.
  Use `--sim` here; don't expect COM ports.
- Cross-platform validated on real hardware: **Windows, Pi CM5 (linux-arm64), Fedora (linux-x64)**.
- A Pi-side Claude session has worked this repo too (`HANDOFF-PI.md`); the two boxes sync via git
  (`main`, two-way pull/push). Keep `main` clean and rebased-friendly.

## Release workflow

`gh` is installed and authed as `gsa700`. A release = git tag + three self-contained zips
(`W2Monitor-win-x64.zip`, `-linux-x64.zip`, `-linux-arm64.zip`) attached to a GitHub release
(asset names must match what the updater expects). Version scheme mirrors the PS app:
`<1.0` = `-beta` (in use, not broadly field-tested); publish as a full "Latest" release so the
updater sees it. Update `CHANGELOG.md` for every release.
