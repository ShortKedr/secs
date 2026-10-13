English | [Русский](BENCHMARK_METHOD_2026-10-11.ru.md)

> **Archived methodology for the 2026-10-11 campaign.** Retained for reproducing `predictive-neutral-v1`, including later V5–V7 notes. The old output paths and analyzer gate apply to the archived workload, not current source. [Current documentation](README.md) | [Current report](REPORT.md).

# SpaceBattle

SpaceBattle is an AI fleet battle and a realistic ECS comparison workload. Desktop and browser clients share the C# simulation; the headless runner can use SECS, LeoECS Lite, LeoECS Classic, Morpeh or StaticEcs.

## Start Here

| Goal | Guide |
| --- | --- |
| Watch the battle in a browser or run it locally | [Web client](../../SpaceBattle.Web/README.md) |
| Run the desktop client and learn its controls | [Desktop client](../../SpaceBattle.Game/README.md) |
| Read measured results, configuration and limits | [Current benchmark report](REPORT_2026-10-11.md) |
| Understand the ECS adapter contract | [Native adapters](../../SpaceBattle.Adapters/README.md) |
| Find and record real combat moments | [Scene search and capture](../../tools/space-battle/scenes/README.md) |
| Explore earlier experiments | [Research index](RESEARCH.md) |

## Current Gameplay

Interactive clients start with 96 escorts and three carriers per team. Ships fight autonomously; you control the camera, speed and viewing tools. Depleted fleets receive warp reinforcements without resetting survivors. The headless comparison is a separate configured workload: use the report's configuration rather than assuming it matches interactive play.

| Rule | Current guide |
| --- | --- |
| LightSwarm roster | [Fleet composition](FLEETS.md) |
| Dispersed startup positions | [Deployment](DEPLOYMENT.md) |
| Target selection, small squads and approach lanes | [Layered squad tactics v3](TACTICS_V3.md) |
| Main weapons and mounted turrets | [Armament](ARMAMENT.md) |
| Shield and hull damage multipliers | [Layered damage](DAMAGE.md) |
| Circle contacts and projectile collision queries | [Physics](PHYSICS.md) |
| Endless corner arrivals | [Reinforcements](REINFORCEMENTS.md) |

These guides describe the current source. Their tuning values are implementation choices, not evidence that matchups are balanced. A performance claim needs the exact rules, configuration, binaries and environment recorded in its report.

## Benchmark Reproduction

Run commands from the repository root. Build and validate before measurement; finish other builds, rendering, profilers and browser tests before timing. Do not mix results from different configurations or revisions.

The full suite measures five ECS implementations at low, nominal and high churn: 108000 measured ticks per case with 600 separate warmup ticks. Run only when intentionally replacing the current campaign's raw outputs:

```powershell
./tools/space-battle/run-suite.ps1 -OutputDirectory docs/space-battle/current
```

`-SkipBuild` reuses the prepared Release host. Short `-Ticks`/`-Warmup` overrides are smoke checks, not a replacement full campaign; give them a separate output directory. The [current report](REPORT_2026-10-11.md) owns the measured configuration, provenance, analysis procedure and comparability result.

Older `predictive-neutral-v1` measurements used Aether and earlier combat rules. Keep them separate: [2026-10-11 report](REPORT_2026-10-11.md), [archived methodology](BENCHMARK_METHOD_2026-10-11.md), [historical systems catalogue](SYSTEMS.md). They do not establish current-source performance. The [cover workflow](COVER.md) produces an illustration, not timing evidence.
