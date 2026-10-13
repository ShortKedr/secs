English | [Русский](RESEARCH.ru.md)

# SpaceBattle Research Index

[Current gameplay and launch guides](README.md) | [Current measured campaign](REPORT.md)

Each study is evidence for its recorded source snapshot, scenario and runtime. Changes to the current game do not refresh old timings. Before/after checks within one runtime do not prove cross-platform determinism; diagnostic stage clocks are not full frontend FPS.

## Archived Campaign and Design

| Document | Context |
| --- | --- |
| [Report, 2026-10-11](REPORT_2026-10-11.md) | Five ECS, `predictive-neutral-v1`, Aether; original tables, charts and capture evidence |
| [Methodology, 2026-10-11](BENCHMARK_METHOD_2026-10-11.md) | Original input schema, gates, metric scopes and analysis instructions; includes later armament notes |
| [Systems catalogue](SYSTEMS.md) | Original nine-stage workload and components; historical catalogue, not a current code inventory |
| [Delivery plan, 2026-10-11](PLAN.md) | Original acceptance requirements and recorded completion ledger; not a current work queue |
| [Tactical AI v2](TACTICS.md) | Earlier kill-cost and focus-group model, superseded by [v3](TACTICS_V3.md) |

## Performance and Compatibility Studies

| Study | Date / scope | Reproduction tools |
| --- | --- | --- |
| [Performance investigation](PERFORMANCE_INVESTIGATION.md) | 2026-10-11; SECS native/WASM diagnostics before mounted armament | [Performance tools](../../tools/performance/README.md) |
| [Spatial optimization](SPATIAL_OPTIMIZATION.md) | 2026-10-12; mounted-armament Aether before/after, before the circle kernel | [Performance tools](../../tools/performance/README.md) |
| [Hardware optimization](HARDWARE_OPTIMIZATION.md) | 2026-10-12; LightSwarm/circle native and Mono AOT replays | [Hardware harness](../../tools/HardwareStudy/README.md) |
| [Portable locality](../../tools/HardwareStudy/LOCALITY_OPTIMIZATION.md) | 2026-10-12; component-only movement and lazy invariants in preserved source | [Hardware harness](../../tools/HardwareStudy/README.md) |
| [Mono compiler study](../../tools/web/MONO_COMPILER_STUDY.md) | 2026-10-12; O3/LTO against the recorded Mono AOT baseline | Commands and frozen manifests in the study |
| [Circle physics](PHYSICS.md) | Current model plus preserved Aether comparison protocol | [Physics harness](../../tools/physics/README.md) |
| [Kernel structural study](../../tools/physics/KERNEL_OPTIMIZATION.md) | Circle-vs-circle prescribed-state experiment; separate from Aether | Reproduction commands in the study |
| [iOS investigation](IOS_WEB.md) | 2026-10-12 snapshot; actual iPhone recovery unverified | Bounded browser checks in the guide |

For screenshots use the [cover workflow](COVER.md); for videos use [scene search](../../tools/space-battle/scenes/README.md). These presentation workflows do not measure throughput. New studies need fresh project-local outputs, preserved baselines and an idle timing window.
