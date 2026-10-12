English | [Русский](README.ru.md)

# SECS Library

<p align="center">
  <img src="docs/branding/shortkedr-ecs-cone.png" width="240" alt="ShortKedr ECS — cedar cone emblem">
</p>

Archetype ECS for .NET 8, with runtime tests, microbenchmarks, and an autonomous
top-down space battle sample.

## Space Battle ECS Comparison

### [Read the benchmark report](docs/space-battle/REPORT.md)

### [Run the live .NET WASM demo](https://shortkedr.github.io/secs/)

[Web source and local reproduction](SpaceBattle.Web/README.md). The browser runs
the shared Core + SECS + Aether simulation, not the full five-framework comparison.
Browser timings are not the desktop benchmark results; cross-platform checksum
equality is not promised.

[**All implementations, benchmarks and tests**](Benchmarks.Local/README.md) |
[**Third-party licenses and source pins**](THIRD_PARTY_NOTICES.md)

Compare SECS, LeoECS Lite, LeoECS Classic, Morpeh and StaticEcs in the same
autonomous space battle. The report covers simulation performance over time,
validation, methodology and reproduction. Results describe one machine and
seed, not a universal ECS winner.

[![Space Battle current gameplay cover](docs/space-battle/cover.png)](docs/space-battle/REPORT.md)

Cover: current gameplay, not the frozen measured run. [Capture workflow](docs/space-battle/COVER.md).

[Benchmark methodology](docs/space-battle/README.md) |
[Simulation systems](docs/space-battle/SYSTEMS.md) |
[Historical WASM diagnostics](docs/space-battle/PERFORMANCE_INVESTIGATION.md) |
[Interactive frontend](SpaceBattle.Game/README.md)

### Space Battle

The sample combines AI steering, eleven ship classes, five weapon types,
compound collision bodies, hull/shields, inventories, thermal management,
carrier production/docking/repair, and inertial damaging debris. AI uses
velocity-based intercept aiming; 25% of firing decisions have deterministic
spread up to +/-0.03 radians. Projectiles and debris are team-neutral and
can hit either faction, including the shooter. SECS drives
the Silk.NET frontend; the headless host also supports LeoECS Lite, LeoECS
Classic, Morpeh and StaticEcs 1.2.01 through native storage/query adapters.
Large ships carry [independent turrets and a heavy hull cannon](docs/space-battle/ARMAMENT.md).
Current gameplay is newer than the frozen benchmark workload.

- [Implementation and acceptance plan](docs/space-battle/PLAN.md)
- [Over-time simulation report](docs/space-battle/REPORT.md)
- [Benchmark methodology and reproduction](docs/space-battle/README.md)
- [Frontend controls and captures](SpaceBattle.Game/README.md)
- [Generated ship assets and prompt](SpaceBattle.Game/Assets/PROVENANCE.md)
- [Native adapter contracts and validation](SpaceBattle.Adapters/README.md)
- [Existing five-framework microbenchmarks](SECS.Benchmarks/README.md)

## Build and Run

From the repository root, keep local tool/package caches in this project:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$PWD/Benchmarks.Local/nuget-packages"
dotnet restore SpaceBattle.Game/SpaceBattle.Game.csproj
dotnet build SpaceBattle.Game/SpaceBattle.Game.csproj -c Release --no-restore -m:1
dotnet SpaceBattle.Game/bin/Release/net8.0/SpaceBattle.Game.dll
```

For the other four ECS adapters, the pinned sources are under
`Benchmarks.Local/vendor` and are included in the repository with their original
licenses. `Benchmarks.Local/fetch.ps1` is an optional verified recovery step, not
a normal clone prerequisite. LeoECS uses conditional MIT-Red terms; see the
[licensing inventory](THIRD_PARTY_NOTICES.md). Headless runs do not initialize
OpenGL or load the generated sprites:

```powershell
dotnet restore SpaceBattle.Headless/SpaceBattle.Headless.csproj
dotnet build SpaceBattle.Headless/SpaceBattle.Headless.csproj -c Release --no-restore -m:1
$env:DOTNET_TieredCompilation = '0'
dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --framework secs --seconds 1800 --output docs/space-battle/custom/custom-secs.json
```

Use a separate output directory for custom runs when reproducing the strict
15-case suite. The analyzer expects only the fixed low/nominal/high campaign
under `docs/space-battle/results`.
