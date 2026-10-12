English | [Русский](README.ru.md)

<p align="center">
  <img src="docs/branding/shortkedr-ecs-emblem.png" width="200" alt="ShortKedr ECS — squirrel and cedar cone emblem">
</p>

# SECS Library

Archetype ECS for .NET 8, with runtime tests, microbenchmarks, and an autonomous
top-down space battle sample. **SECS** stands for **ShortKedr ECS**.

[Quickstart](#quickstart) · [Space Battle](#space-battle) ·
[Benchmarks](#benchmarks) · [Documentation](#documentation)

## The Library

- **Archetype storage:** unmanaged components grouped into configurable chunks.
- **Typed queries:** update components by `ref` with `IQueryProcessor`, or iterate chunks directly.
- **Entity lifecycle:** create and destroy entities; add, read, update, and remove components.
- **System stages:** register work in `PreUpdate`, `Update`, and `PostUpdate`.

## Quickstart

Use a .NET SDK that supports .NET 8. Run from the repository root; these settings
keep tool and package caches inside the project:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$PWD/Benchmarks.Local/nuget-packages"

dotnet run --project SECS.Sample/SECS.Sample.csproj -c Release
dotnet test SECS.Tests/SECS.Tests.csproj -c Release
```

To use the library in your own .NET 8 application, add a project reference to
[SECS.csproj](SECS/SECS.csproj). The [sample source](SECS.Sample/Program.cs)
demonstrates entities, queries, systems, and chunk iteration.

<details>
<summary>A minimal typed query</summary>

```csharp
using SECS;

using var world = new World();
var entity = world.CreateEntity();
world.Add(entity, new Position { X = 0, Y = 0 });
world.Add(entity, new Velocity { X = 3, Y = 1 });

var movement = new Movement();
world.Query<Position, Velocity>().ForEach(ref movement);

var position = world.Get<Position>(entity);
Console.WriteLine($"Position: ({position.X}, {position.Y})");

struct Position { public float X, Y; }
struct Velocity { public float X, Y; }

struct Movement : IQueryProcessor<Position, Velocity>
{
    public void Execute(Entity entity, ref Position position, ref Velocity velocity)
    {
        position.X += velocity.X;
        position.Y += velocity.Y;
    }
}
```

</details>

## Space Battle

**[Play the browser demo](https://shortkedr.github.io/secs/)** ·
**[Read the benchmark report](docs/space-battle/REPORT.md)** ·
[Desktop controls](SpaceBattle.Game/README.md)

Autonomous fleets combine AI steering, intercept aiming, turrets, shields,
carrier production, docking, and repair. The Silk.NET desktop frontend runs
SECS; the headless host also supports LeoECS Lite, LeoECS Classic, Morpeh, and
StaticEcs through native storage and query adapters.

[![Space Battle current gameplay](docs/space-battle/cover.png)](docs/space-battle/REPORT.md)

*Current gameplay, newer than the frozen benchmark workload.
[Capture workflow](docs/space-battle/COVER.md).*

The browser runs the shared simulation with SECS; its timings are separate
from the desktop benchmark. Cross-platform checksum equality is not promised.
See [Web source and reproduction](SpaceBattle.Web/README.md).

### Run Locally

Use the cache settings from Quickstart. The desktop client needs an
OpenGL 3.3-capable environment; the headless host does not initialize OpenGL
or load generated sprites.

<details>
<summary>Desktop client</summary>

```powershell
dotnet restore SpaceBattle.Game/SpaceBattle.Game.csproj
dotnet build SpaceBattle.Game/SpaceBattle.Game.csproj -c Release --no-restore -m:1
dotnet SpaceBattle.Game/bin/Release/net8.0/SpaceBattle.Game.dll
```

</details>

<details>
<summary>Headless simulation</summary>

```powershell
dotnet restore SpaceBattle.Headless/SpaceBattle.Headless.csproj
dotnet build SpaceBattle.Headless/SpaceBattle.Headless.csproj -c Release --no-restore -m:1
$env:DOTNET_TieredCompilation = '0'
dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --framework secs --seconds 1800 --output docs/space-battle/custom/custom-secs.json
```

Keep custom output outside `docs/space-battle/results`: the strict 15-case
analyzer expects only the fixed low/nominal/high campaign there.

</details>

## Benchmarks

| Workload | Results and reproduction |
| --- | --- |
| Create, iterate, churn, and destroy entities | [Microbenchmarks](SECS.Benchmarks/README.md) |
| Autonomous battle over time | [Space Battle report](docs/space-battle/REPORT.md) |
| Five-framework setup and validation | [Local comparison guide](Benchmarks.Local/README.md) |

Recorded results describe the measured machine, runtime, framework revisions,
and workloads. They do not establish a universal ECS winner. The local
five-framework comparison uses a Stopwatch harness; the SECS-only suite uses
BenchmarkDotNet. See the linked guides for methodology and raw results.

## Documentation

| Topic | Guide |
| --- | --- |
| Library performance | [SECS optimizations](docs/SECS_OPTIMIZATION.md) |
| Determinism | [Test and GitHub Actions plan](docs/SECS_DETERMINISM_PLAN.md) — implementation plan |
| Battle design | [Implementation plan](docs/space-battle/PLAN.md) · [Simulation systems](docs/space-battle/SYSTEMS.md) · [Armament](docs/space-battle/ARMAMENT.md) |
| ECS adapters | [Contracts and validation](SpaceBattle.Adapters/README.md) |
| Art and captures | [Ship assets and prompts](SpaceBattle.Game/Assets/PROVENANCE.md) · [Cover workflow](docs/space-battle/COVER.md) |
| Browser performance | [Historical WASM diagnostics](docs/space-battle/PERFORMANCE_INVESTIGATION.md) |
| Third-party code | [Licenses and pinned sources](THIRD_PARTY_NOTICES.md) |

Pinned sources for the four other ECS adapters are included under
`Benchmarks.Local/vendor` with their original licenses. `Benchmarks.Local/fetch.ps1`
is an optional recovery step. LeoECS uses conditional MIT-Red terms; consult
the licensing inventory before reuse.
