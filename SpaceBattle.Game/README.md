English | [Русский](README.ru.md)

# SpaceBattle Desktop

Watch an autonomous fleet battle in the Silk.NET/OpenGL client. It runs the shared C# simulation with SECS. You control the view and simulation speed; the AI controls the ships.

[SpaceBattle overview](../docs/space-battle/README.md) | [Current benchmark report](../docs/space-battle/REPORT.md) | [Engineering guide](DEVELOPMENT.md)

## Run

From the repository root, with .NET 8 and an OpenGL 3.3 capable device:

```powershell
dotnet build SpaceBattle.Game/SpaceBattle.Game.csproj -c Release
./SpaceBattle.Game/bin/Release/net8.0/SpaceBattle.Game.exe
```

The default battle starts with 96 escorts and three carriers per team (198 ships). It uses the LightSwarm roster, dispersed deployment, layered squad AI, mounted guns and circle physics. Depleted fleets receive corner warp reinforcements. See the [gameplay guides](../docs/space-battle/README.md#current-gameplay) for the rules.

Useful launch options:

| Option | Meaning |
| --- | --- |
| `--seed 7331` | Repeat a seeded starting scenario |
| `--ships-per-team 96` (also `--ships`) | Initial escorts per team, excluding carriers |
| `--finite` | Disable endless warp reinforcement waves |
| `--width 1440 --height 900` | Window size; minimum 960 × 540 |
| `--statistics` | Open the detailed statistics panel |
| `--colliders` | Start with collision outlines visible |
| `--follow-ship 22` | Start following a ship by serial |

`--ships 2048 --legacy-deployment` is a deliberately dense stress scenario, not the default experience.

## Controls

| Action | Input |
| --- | --- |
| Pause / resume | Space or HUD button |
| Change simulation speed | + / − or HUD buttons |
| Restart the same seeded battle | R or HUD button |
| Fit arena | Home or HUD button |
| Pan | WASD / arrows, middle or right drag |
| Zoom at cursor | Mouse wheel |
| Select a ship | Left click |
| Follow selected ship | C or crosshair button |
| Toggle colliders | F2 or bounding-box button |
| Toggle statistics | F3 or chart button |

Following frames the watched ship and its live target. Death switches to a known surviving fatal projectile owner, otherwise a nearby survivor. Manual camera movement, selection elsewhere, Fit or restart cancels follow. Camera actions do not affect combat.

The statistics panel separates simulation timing, entity populations, combat counters and storage. Mean uses the last 600 simulation ticks (10 seconds at 60 Hz), including physics and commits. Capture and rendering sit outside this timer. Compare it with the [game preset and HUD-window report](../docs/space-battle/GAME_REPORT.md); a full-run mean for a finite battle averages a different workload and period.

## Capture and Diagnostics

Use the [engineering guide](DEVELOPMENT.md) for framebuffer captures, visual checks, live-statistics validation and CPU/GPU profiling. [Asset provenance](Assets/PROVENANCE.md) records the sprite contracts. [Scene tools](../tools/space-battle/scenes/README.md) find and record real combat episodes.

Keep new captures and profiles in a fresh `Benchmarks.Local/tmp` directory. Finish rendering, builds and validation before starting benchmark timing.
