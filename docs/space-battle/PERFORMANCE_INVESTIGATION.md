English | [Русский](PERFORMANCE_INVESTIGATION.ru.md)

# Space Battle Performance Investigation

<!-- research-colors:start -->
### Colour scale

![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") up to 5% · ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 15% · ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") from 30% · ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") neutral.

Colour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules](../benchmarks/COLORS.md).


Only selected metrics within one scenario/window/environment are compared. Measured comparisons stay grey when neither the table nor its linked summary supplies independent-repeat envelopes. Stage shares and gameplay counters are not colored.
<!-- research-colors:end -->

[Simulation report](REPORT_2026-10-11.md)

Local diagnostic study, 2026-10-11. This is not a replacement for the published five-ECS benchmark. Only the SECS game port was measured. No original measurements, production gameplay sources, deployment settings or remote state were changed.

These archived diagnostics predate [mounted armament](ARMAMENT.md). Their timings
do not measure the current gun-enabled gameplay.

The later [spatial optimization study](SPATIAL_OPTIMIZATION.md) records the next implementation and its before/after results. It is also a historical snapshot.

## Findings

- The dense battle does reproduce roughly 24-27 ms of WASM simulation work, before frame export or drawing.
- The tactical AI measured here is not the dominant cost at the nominal population. It takes about 0.32 ms in the first 600 ticks. Hits, entity collection and lifecycle dominate.
- This problem already exists before the tactical update. The new AI changes battle trajectories and populations, so revision timings are not identical-workload microbenchmarks.
- Desktop is substantially faster, but has frame-time spikes. Its cold-start replay averages about 7.5 ms per CPU frame, with P95 around the 60 FPS budget.

## Protocol

Revisions: `6150700` (before tactics), `c51390c` (tactical AI), `d853ae7` (live statistics). Nominal profile, seed 7331, 96 escorts and three carriers per team initially, full compound ship physics, neutral projectiles and debris, particles enabled.

Three independent fresh replays per revision. Native warms a separate battle for 1,200 ticks; WASM warms a separate battle for 600 ticks and then resets. Measurements cover ticks 1-600 and 601-1,200. A tick is not an independent repeat. Tables report the median of the three run means, with run-mean ranges where useful, not the fastest sample. Stage medians are computed separately and need not sum exactly to the median total.

Native: .NET 8.0.31 CoreCLR, Release. Browser: .NET 8.0.31 Mono AOT, SDK 8.0.425, Chromium 151.0.7922.34 headless, 1440x900, 24 visible logical processors. This host's [hardware inventory](hardware.json) identifies a Ryzen 9 9900X. Builds and timed runs were not overlapped. Background applications, clock changes and headless rendering remain sources of uncertainty.

Browser comparison uses production `Advance(1)` plus binary frame export, with Canvas/DOM paused. The only baseline browser-source addition is an on-demand `DiagnosticStage(int)` export reading existing stage timers outside their timing scope. Revision order alternates. Native core timings exclude snapshots, rendering and frontend statistics.

Checksums and populations reproduce across repeats within each runtime. Native and WASM checksums differ: these runs do **not** establish cross-platform bitwise lockstep. This also prevents interpreting their timing ratio as a pure compiler overhead measurement.

Raw data: [native before](performance-study/native-before.json), [native tactics](performance-study/native-tactics.json), [native statistics](performance-study/native-stats.json), [browser revisions](performance-study/web.json), [summary](performance-study/summary.json).

## Revision Comparison

All values are milliseconds per tick. Export includes capture, binary packing and the JS call, not Canvas drawing.

| Revision | Native 1-600 | WASM 1-600 | WASM with export 1-600 | WASM 601-1200 |
| --- | ---: | ---: | ---: | ---: |
| Before tactics | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 1.788 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 14.583 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 19.574 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 8.500 |
| Tactical AI | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 1.742 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 13.110 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 16.384 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 4.950 |
| Live statistics | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 1.716 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 12.477 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 15.625 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 4.777 |

Early WASM run-mean ranges: 14.149-14.796, 12.659-13.190 and 12.111-12.578 ms respectively. Native early ranges: 1.647-1.823, 1.722-1.784 and 1.649-1.735 ms. Do not attribute the small tactics/statistics timing difference to an optimization: their gameplay checksums match and the simulation algorithm is unchanged by statistics.

Early browser P95: 25.5, 26.6 and 25.5 ms respectively. A 12.5 ms whole-window mean does not imply the dense middle of that window meets a 16.67 ms budget.

Average populations in ticks 1-600, before versus after tactics: ships 121.6 versus 93.4, projectiles 316.6 versus 227.6, debris 805.8 versus 804.3, particles 1,855.3 versus 1,871.9. These are workload descriptors, not good/bad outcomes. Tactical targeting kills ships earlier in this seeded replay; lower later cost is not proof that the AI implementation itself is cheaper.

## Bottlenecks in the Measured Revision

WASM revision `d853ae7` (live statistics), ticks 1-600:

| Stage | Median run mean, ms | Approximate share |
| --- | ---: | ---: |
| Hits | 5.701 | 46% |
| Lifecycle, including final collection | 2.307 | 18% |
| Initial collection | 2.201 | 18% |
| Physics and synchronization | 1.552 | 12% |
| AI, including squad preparation | 0.324 | 3% |
| All remaining stages | about 0.36 | about 3% |

`Sweep` in [PhysicsService](../../SpaceBattle.Core/PhysicsService.cs) performs five parallel rays for a moving projectile or fragment. Each ray first queries its origin AABB, then raycasts. Hundreds of projectiles plus roughly a thousand fragments can therefore issue thousands of tree traversals per tick. This is an existing hotspot, not a newly added all-ships collision scan.

[Battle.Step](../../SpaceBattle.Core/Battle.cs) rebuilds and serial-sorts four entity lists at the beginning and end of each tick. [Capture](../../SpaceBattle.Core/BattleInspection.cs) repeats collection for rendering. The lifecycle bucket includes the second collection, so it must not be interpreted as destruction cost alone.

Particles/projectiles/debris are created with `Create` followed by three component additions. The SECS adapter migrates through intermediate archetypes. Access also repeatedly translates entity identities and checks liveness. These are measured-stage optimization candidates, not evidence that one ECS is intrinsically worse than another.

Tactical AI has avoidable work: full target scoring before decision gates, wide snapshots rebuilt each tick, repeated threat/cover searches, squad writeback and repair scans. At nominal scale it is a secondary target. Larger populations and damaged/dispersed squads need separate stress tests.

## Dense Web And Desktop

The [full-browser probes](performance-study/web-live-1.json) advance to tick 180, sample 40 direct ticks (181-220), measure export separately, force 30 frontend frames, then observe five seconds of live RAF. [Repeat 2](performance-study/web-live-2.json), [repeat 3](performance-study/web-live-3.json).

Core means: 23.885 / 26.930 / 26.572 ms. Core plus export: 29.790 / 33.852 / 33.565 ms. Export-only at the later frozen frame: 4.338 / 5.550 / 5.117 ms. Export-only is a different tick window, so subtracting it from the earlier total is invalid. Live simulation rates later in the battle: 55.9 / 50.9 / 54.3 ticks/s. Render rates: 56.1 / 51.1 / 54.5 FPS. Those live rates are not rates for the frozen peak: the battle changes during the five-second observation.

Desktop [replay 1](performance-study/desktop-1.json), [replay 2](performance-study/desktop-2.json), [replay 3](performance-study/desktop-3.json): fresh process, no warmup, exactly 600 steps/rendered frames, 1440x900, VSync off, full scene/HUD submission. Median run means: simulation 3.237 ms, capture 0.514 ms, render submission 2.201 ms, total CPU frame 7.469 ms. CPU-frame P95 ranges 14.770-17.118 ms. The existing replay profiler disables live statistics recording/refresh, measures uncapped replay rather than real-time gameplay FPS, and separates presentation/GPU timing. It does not prove every live desktop frame is below budget.

## Build And Web Approach

Release publishing compiles 47 assemblies, including `SECS`, `SpaceBattle.SECS`, `SpaceBattle.Core`, `Aether.Physics2D` and generic AOT instances. Actual generated build commands enable SIMD and native WASM exception handling. Assembly bitcode and final WASM use `-O2`; native runtime glue uses `-Oz`. The latter is not evidence that the entire game was compiled for size. The game loop uses a reusable binary buffer, not per-frame JSON.

The deployed Pages site was also [probed read-only](performance-study/live-runtime.json): `aot-binary`, `tactical-lock-v2`, binary protocol v2, 198 initial ships, `crossOriginIsolated=false`. These flags do not prove that every dynamically reached generic method avoids interpretation; no named method-level CPU trace was captured.

`-O3`, LTO and a newer runtime were not measured in this study. Mono AOT is not evidence of desktop CoreCLR performance. Without a named CPU trace, the remaining gap cannot be assigned to generic dispatch, bounds checks, runtime helpers or interpreter fallback in particular methods.

IL stripping and removing unused Blazor hosting code mainly target download/startup size. They are not demonstrated solutions for this CPU-bound tick. [Microsoft runtime guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/webassembly-runtime-performance?view=aspnetcore-10.0).

The inspected Pages response did not provide COOP/COEP, which managed multithreading requires. A 24 ms tick alone exceeds a 60 Hz budget. Enabling managed profilers changes performance and size, so diagnostic traces are not comparable with the release timing here. [Runtime feature documentation](https://github.com/dotnet/runtime/blob/main/src/mono/wasm/features.md); [Microsoft profiler documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/webassembly-browser-developer-tools-diagnostics?view=aspnetcore-10.0).

## Isolated Optimization Experiment

A conservative swept-AABB empty-space test was added **only to an archived copy** of `PhysicsService`. An eligible candidate retains the exact original five casts. Empty space returns the original miss outputs. The bounds enclose rounded ray endpoints and the original origin-query epsilon, with outward rounding; non-finite bounds fall back to the original path. No team, owner or hull filter was added.

[Paired browser runs](performance-study/web-sweep-comparison.json), three independent replays per variant in alternating order:

| Metric, ticks 1-600 | Original | Experimental |
| --- | ---: | ---: |
| Core mean, ms | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 12.555 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 9.442 |
| Hits mean, ms | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 5.746 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 2.625 |
| Core plus export mean, ms | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 15.726 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 12.560 |
| Core P95, ms | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 26.8 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 20.0 |

Core run-mean ranges: original 12.327-12.927 ms, experimental 9.200-9.625 ms. This is about 25% less core time and 54% less hits time. Later core means are 4.783 versus 3.910 ms. These are observed effects, not a statistical-significance claim or a guarantee for other scenes.

[Native experimental runs](performance-study/native-sweep.json) have a median early mean of 1.278 ms versus the earlier baseline's 1.716 ms. These native samples were sequential, not paired; prefer the paired browser result for the speed claim.

21,518 native differential sweeps matched hit/miss, serial and impact-coordinate bits against the unfiltered original path: empty space, stationary/zero-radius rays, origin overlap, compound fixtures, disabled fixtures, removal/slot reuse and large-coordinate cases. Replays retained the original populations, counters and checksums at ticks 600/1200 in both runtimes. This is not exhaustive proof: additional seeds, moving-target boundary cases and long-running low/high profiles were not measured.

[Experimental patch](../../tools/performance/sweep-empty-space.patch) and [differential harness](../../tools/performance/SweepCheck.cs) are retained for reproduction; the patch is **not applied** to the production project. The remaining 20 ms core P95 still misses 60 Hz even before drawing/export.

The dense full-browser probe repeated on the experiment ([run 1](performance-study/web-sweep-live-1.json), [run 2](performance-study/web-sweep-live-2.json), [run 3](performance-study/web-sweep-live-3.json)) measured 16.375 / 16.398 / 16.583 ms for ticks 181-220, versus 23.885 / 26.930 / 26.572 ms previously. Median core-plus-export means were 21.918 versus 33.565 ms. These full-frontend series were sequential, not paired, and still do not meet the full 60 Hz frame budget in the dense interval. Later live tick rates were 59.5-59.9 ticks/s as the workload evolved.

Reproduction tools: [native harness](../../tools/performance/Program.cs), [browser harness](../../tools/web/profile-revisions.mjs), [tool instructions](../../tools/performance/README.md).
