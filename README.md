English | [Русский](README.ru.md)

**[Space Battle promo: four recorded clips](docs/promo/space-battle/README.md)**

<p align="center">
  <img src="docs/branding/logo/shortkedr-ecs-200.png" width="200" height="200" alt="ShortKedr ECS — squirrel and cedar cone emblem">
</p>

# SECS — ShortKedr ECS

A small entity-component-system (ECS) library with .NET Standard 2.0/2.1 and
.NET 8 builds. SECS groups entities
by their component types and stores unmanaged component data in chunks. Systems update
that data through typed `ref` queries and run sequentially in `PreUpdate`,
`Update`, and `PostUpdate`. The repository includes tests, benchmarks, and an
autonomous Space Battle sample.

## Start here

| Your goal | Where to go |
| --- | --- |
| Use the library | [Quickstart](#quickstart) · [Package installation](docs/secs/INSTALL.md) · [API and lifetime rules](docs/secs/README.md) · [Sample source](SECS.Sample/Program.cs) |
| Work on SECS | [Implemented optimizations](docs/SECS_OPTIMIZATION.md) · [Tests](SECS.Tests) |
| Read or reproduce measurements | [Microbenchmarks](SECS.Benchmarks/README.md) · [Five-framework comparison](Benchmarks.Local/README.md) · [Space Battle report](docs/space-battle/REPORT.md) |
| Run the game | [Desktop](SpaceBattle.Game/README.md) · [Web](SpaceBattle.Web/README.md) |

`Benchmarks.Local/` is the public five-framework comparison harness: source code,
reproduction scripts, and measured results. "Local" describes how the runner is
used, not the repository's visibility. Its build and cache directories are ignored;
internal plans are kept in `secs-private/`.

## Quickstart

Use a .NET SDK that supports .NET 8 and run from the repository root. These
PowerShell settings keep tool and package caches inside the project:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$PWD/Benchmarks.Local/nuget-packages"

dotnet run --project SECS.Sample/SECS.Sample.csproj -c Release
dotnet test SECS.Tests/SECS.Tests.csproj -c Release
```

To use SECS in your application, add a project reference to
[SECS.csproj](SECS/SECS.csproj). The [library guide](docs/secs/README.md)
contains a minimal query example and explains entity ownership, component
references, structural changes, and chunk iteration.

## Space Battle

**[Play in your browser](https://shortkedr.github.io/skecs/)** ·
**[Read the benchmark report](docs/space-battle/REPORT.md)**

Autonomous fleets use AI steering, intercept aiming, turrets, shields, carrier
production, docking, and repair. The desktop and browser frontends use SECS;
the headless host also compares LeoECS Lite, LeoECS Classic, Morpeh, and StaticEcs
through native storage and query adapters.

[![Space Battle gameplay](docs/space-battle/cover.png)](docs/space-battle/README.md)

*See the report for the measured workload, configuration, and source revisions.*

## Status and evidence

The library currently runs on CPU with sequential systems and queries.
Current unit tests and game checksums do not establish cross-platform parity.
Benchmark results describe particular machines, revisions, and workloads;
see each report for its method and limitations.

[License: MIT for original project material](LICENSE) ·
[Documentation map](docs/README.md) ·
[Internal plans (restricted access)](https://github.com/ShortKedr/secs-private) ·
[Third-party licenses and pinned sources](THIRD_PARTY_NOTICES.md)
