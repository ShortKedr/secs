English | [Русский](RESEARCH.ru.md)

# SpaceBattle Research Index

[Current gameplay and launch guides](README.md) | [Current measured campaign](REPORT.md)

Read each study with its recorded source revision, scenario and runtime. Later game changes do not update those measurements. Matching before/after results within one runtime do not prove cross-platform determinism. Stage timings measure parts of the simulation; they do not measure the complete client's FPS.

## Archived campaign

| Document | Context |
| --- | --- |
| [Report, 2026-10-11](REPORT_2026-10-11.md) | Five ECS, `predictive-neutral-v1`, Aether; original tables, charts and capture evidence |
| [Systems catalogue](SYSTEMS.md) | Original nine-stage workload and components; historical catalogue, not a current code inventory |
| [Tactical AI v2](TACTICS.md) | Earlier kill-cost and focus-group model, superseded by [v3](TACTICS_V3.md) |

## Historical Visual Evidence

These PNG/sidecar pairs are recorded historical snapshots, not current capture outputs. Their reproduction from the current source has not been verified.

| Snapshot | PNG | Recorded metadata |
| --- | --- | --- |
| Tick 0 | [tick-0.png](tick-0.png) | [sidecar](tick-0.png.json) |
| Tick 900 | [tick-900.png](tick-900.png) | [sidecar](tick-900.png.json) |
| Tick 900, 960 x 540 | [tick-900-960x540.png](tick-900-960x540.png) | [sidecar](tick-900-960x540.png.json) |
| Tick 3600 | [tick-3600.png](tick-3600.png) | [sidecar](tick-3600.png.json) |
| Stress 4102, tick 0 | [stress-4102-tick-0.png](stress-4102-tick-0.png) | [sidecar](stress-4102-tick-0.png.json) |

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
| [iOS investigation](IOS_WEB.md) | 2026-10-12 snapshot; actual iPhone recovery unverified | Local evidence and measured results in the study |

For screenshots use the [cover guide](COVER.md); for videos use [scene search](../../tools/space-battle/scenes/README.md). These presentation workflows do not measure throughput.
