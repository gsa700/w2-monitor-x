# W2 Monitor X (cross-platform)

Cross-platform rewrite of [W2 Monitor](../W2Monitor) in **.NET 8 + Avalonia**, targeting
Windows, Linux (x64), and **Raspberry Pi (linux-arm64)**. Built on the same stack as the
[LP-100A project](../LP100A).

> **Status:** Phase 0 scaffold — it builds and shows a window on all three targets; no
> meter is wired up yet. See the porting plan in the PowerShell repo:
> `../W2Monitor/PORTING-PLAN-Avalonia.md`.

## Layout

```
src/
  W2.Core/   # no UI: serial reader + W2 query/response protocol (9600 8N1)
  W2.App/    # Avalonia MVVM: views, view-models, services (PortIdentity, config)
```

## Build & run

```sh
dotnet build
dotnet run --project src/W2.App

# No W2 on the bench? Drive the UI from a built-in synthetic meter:
dotnet run --project src/W2.App -- --sim
```

## Publish (self-contained)

```sh
dotnet publish src/W2.App -c Release -r win-x64      --self-contained -p:PublishSingleFile=true -o publish/win-x64
dotnet publish src/W2.App -c Release -r linux-x64    --self-contained -p:PublishSingleFile=true -o publish/linux-x64
dotnet publish src/W2.App -c Release -r linux-arm64  --self-contained -p:PublishSingleFile=true -o publish/linux-arm64
```

## License

GPLv3 — see [LICENSE](LICENSE). Created by David Erickson (AB0R).
