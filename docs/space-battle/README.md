English | [Русский](README.ru.md)

**[Watch four promo clips](../promo/space-battle/README.md)**

# SpaceBattle

SpaceBattle is an AI fleet battle and a realistic ECS comparison workload. Desktop and browser clients share the C# simulation; the headless runner can use SECS, LeoECS Lite, LeoECS Classic, Morpeh or StaticEcs.

## Start Here

| Goal | Guide |
| --- | --- |
| Watch in a browser or run locally | [Web client](../../SpaceBattle.Web/README.md) |
| Run desktop and learn its controls | [Desktop client](../../SpaceBattle.Game/README.md) |
| Read the primary high workload and controlled ECS churn comparison | [Current benchmark report](REPORT.md) |
| Read the previous game preset campaign | [Previous game report](GAME_REPORT.md) |
| Understand the current stages, components and mutation boundaries | [Current architecture](ARCHITECTURE.md) |
| Understand the ECS adapter contract | [Native adapters](../../SpaceBattle.Adapters/README.md) |
| Find and record real combat moments | [Scene search and capture](../../tools/space-battle/scenes/README.md) |
| Explore earlier experiments | [Research index](RESEARCH.md) |

## Current Gameplay

Interactive clients start with 96 escorts and three carriers per team. Ships fight autonomously; you control the camera, speed and viewing tools. Depleted fleets receive warp reinforcements without resetting survivors. Headless offers two presets: `game` uses the interactive fleet, deployment and reinforcements; `benchmark` retains the finite battle for historical comparisons. The primary performance campaign uses `game` with high battle intensity.

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

The primary suite measures five ECS implementations on one fixed high intensity game battle. Its baseline has no synthetic component churn; two additional profiles perform one or four `Lifetime` component transitions per tick on a fixed pool of inert entities. Combat rules, fleet size, seed and reinforcements stay identical across all three profiles. Each case measures 108000 ticks after 600 separate warmup ticks:

```powershell
./tools/space-battle/run-suite.ps1 -OutputDirectory docs/space-battle/current
```

`-SkipBuild` reuses the prepared Release host. Short `-Ticks`/`-Warmup` overrides are smoke checks; give them a separate output directory. The current report records full Step totals, nine stage means, 600-tick HUD windows, and short opening phases. The [previous game report](GAME_REPORT.md) and [finite battle report](REPORT_2026-10-14_FINITE.md) used a gameplay multiplier to vary churn, so their profile timings describe different battle trajectories.

The [2026-10-11 report](REPORT_2026-10-11.md) preserves the `predictive-neutral-v1` Aether campaign, its measurements and analysis scope. Its source links navigate to current files; they do not restore the historical implementation. No verified source snapshot or command for an exact historical rerun is identified here, and current suite commands produce different workloads. See the [historical systems catalogue](SYSTEMS.md) for the measured composition. These results do not establish current-source performance. The [cover workflow](COVER.md) produces an illustration, not timing evidence.
