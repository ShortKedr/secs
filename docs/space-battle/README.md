English | [Русский](README.ru.md)

# SpaceBattle

SpaceBattle is an AI fleet battle and a realistic ECS comparison workload. Desktop and browser clients share the C# simulation; the headless runner can use SECS, LeoECS Lite, LeoECS Classic, Morpeh or StaticEcs.

## Start Here

| Goal | Guide |
| --- | --- |
| Watch in a browser or run locally | [Web client](../../SpaceBattle.Web/README.md) |
| Run desktop and learn its controls | [Desktop client](../../SpaceBattle.Game/README.md) |
| Read game timings, stages and HUD comparisons | [Game preset report](GAME_REPORT.md) |
| Read the finite battle comparison | [Benchmark preset report](REPORT.md) |
| Understand the current stages, components and mutation boundaries | [Current architecture](ARCHITECTURE.md) |
| Understand the ECS adapter contract | [Native adapters](../../SpaceBattle.Adapters/README.md) |
| Find and record real combat moments | [Scene search and capture](../../tools/space-battle/scenes/README.md) |
| Explore earlier experiments | [Research index](RESEARCH.md) |

## Current Gameplay

Interactive clients start with 96 escorts and three carriers per team. Ships fight autonomously; you control the camera, speed and viewing tools. Depleted fleets receive warp reinforcements without resetting survivors. Headless offers two explicit presets: `game` matches the default fleet, deployment and reinforcement settings; `benchmark` retains the finite battle for continuity.

| Rule | Current guide |
| --- | --- |
| LightSwarm roster | [Fleet composition](FLEETS.md) |
| Dispersed startup positions | [Deployment](DEPLOYMENT.md) |
| Target selection, small squads and approach lanes | [Layered squad tactics v3](TACTICS_V3.md) |
| Main weapons and mounted turrets | [Armament](ARMAMENT.md) |
| Shield and hull damage multipliers | [Layered damage](DAMAGE.md) |
| Circle contacts and projectile collision queries | [Physics](PHYSICS.md) |
| Endless corner arrivals | [Reinforcements](REINFORCEMENTS.md) |

Tuning values are implementation choices, not proof of balanced matchups. A performance claim needs the rules, configuration, binaries and environment recorded in its report.

## Benchmark Reproduction

Run from the repository root. Build and validate before measurement; finish other builds, rendering, profilers and browser tests before timing. Do not mix configurations or revisions.

Each full suite measures five ECS implementations at low, nominal and high churn: 108000 measured ticks per case with 600 separate warmup ticks. Run the suites sequentially when replacing the published raw outputs:

```powershell
./tools/space-battle/run-suite.ps1 -OutputDirectory docs/space-battle/current
```

Add `-Preset game -OutputDirectory docs/space-battle/game-current` for the default gameplay workload. `-SkipBuild` reuses the prepared Release host. Short `-Ticks`/`-Warmup` overrides are smoke checks; give them a separate output directory. Both reports record full Step totals, nine stage means and 600-tick windows using the same timer as the desktop HUD.

Earlier `predictive-neutral-v1` measurements used Aether and earlier combat rules. Keep them separate: [2026-10-11 report](REPORT_2026-10-11.md), [archived methodology](BENCHMARK_METHOD_2026-10-11.md), [historical systems catalogue](SYSTEMS.md). They do not establish current-source performance. The [cover workflow](COVER.md) produces an illustration, not timing evidence.
