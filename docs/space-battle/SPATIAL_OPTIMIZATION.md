English | [Русский](SPATIAL_OPTIMIZATION.ru.md)

# Space Battle Spatial Optimization

<!-- research-colors:start -->
### Colour scale

![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") up to 5% · ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 15% · ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") from 30% · ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") neutral.

Colour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules](../benchmarks/COLORS.md).


Only selected metrics within one scenario/window/environment are compared. Measured comparisons stay grey when neither the table nor its linked summary supplies independent-repeat envelopes. Stage shares and gameplay counters are not colored.
<!-- research-colors:end -->

[Simulation report](REPORT_2026-10-11.md) | [Earlier investigation](PERFORMANCE_INVESTIGATION.md)

Historical study, 2026-10-12, of mounted-armament gameplay with Aether before the circle-kernel replacement. This measures SECS before/after algorithm changes, not a new ranking of the five ECS frameworks. Frozen benchmark evidence and remote state are unchanged.

## What Was Wasteful

- Every projectile and fragment invoked up to five raycasts and five origin-overlap queries, including trajectories through empty space. Aether already had a broadphase tree; the simulation was repeatedly traversing it.
- Moving entities were queried, collected and sorted at both ends of each tick, then queried and sorted again for frame capture. Most membership changes were already known from spawn/retirement requests.
- Threat search scanned all ships repeatedly, including nested calls from cover selection. Formation, nearby cover and turret acquisition scanned unrelated ships outside their relevant ranges.
- Every docking request recounted occupied slots; every build recounted the carrier's children. Each destroyed hull scanned the entire turret list and removed elements with repeated list shifts.

## Changes And Complexity

`S` = ships, `G` = squads, `T` = turrets, `E` = entities, `K` = nearby candidates, `Q` = docking requests, `B` = build requests, `D` = destroyed hulls, `A` = their attached guns.

| Path | Before | Now |
| --- | --- | --- |
| Empty swept trajectory | Up to 5 rays + 5 point queries | One conservative AABB query; no narrow phase on an empty result |
| Threats across one snapshot | Repeated full `O(S)` scans, potentially nested | `O(S)` reverse-index build, each incoming edge visited at most once by memoized threat searches |
| Formation and cover | Full fleet/squad scans | Point/leader grids; exact rules checked on `K` candidates in original tie order |
| Turret target acquisition | `O(T*S)` full scans | Bounded candidates per searching turret |
| Dock/build accounting | `O(Q*S + B*S)` | `O(S+Q)` only when docking is requested; `O(S+B)` only when builds exist |
| Attachment destruction | `O(D*T)` plus repeated shifts | One current-parent index rebuild when needed, `O(T+A)`, then one list compaction |
| End-of-tick moving lists | ECS queries and `O(E log E)` sorting | Stable `O(E)` compaction; normal increasing-serial spawns need no sorting |
| Frame capture after Step | Extra collection and sorting | Reuses finalized lists; frame packing remains `O(E)` |

Candidate ordering costs `O(K log K)` when grid results span unordered buckets. Buckets and query buffers are reused. The grid is rebuilt from the immutable pre-decision snapshot, not from partially updated AI rows. Equal-distance tie breaks, target leases, RNG, neutral/friendly/self hits and serial-ordered lifecycle transitions remain unchanged.

The hit prefilter encloses the actual floating-point endpoints of all five rays and their origin queries, including rounding slack. It only rejects empty broadphase results. The original narrow phase is retained for candidates: this does not turn the approximate five-ray CCD into an exact swept disk or add moving-target CCD.

`Capture(frame)` still synchronizes after direct Store edits. Native/web frontends use `Capture(frame, refreshEntities: false)` only with lists finalized by construction or `Step`. Direct Store edits require the default refreshing capture. Mutable mount ownership is indexed from current components before a destruction batch, not assumed immutable.

## Measurements

Milliseconds per tick/call; reduction is CPU time saved, not an FPS claim.

| Scenario | Before | After | Time Reduction |
| --- | ---: | ---: | ---: |
| Native, 96 escorts/team, ticks 1-600 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 1.704 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 1.025 | 40% |
| WASM core, ticks 1-600 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 10.173 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 6.053 | 40% |
| WASM core + binary export, ticks 1-600 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 12.875 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 6.925 | 46% |
| WASM export + interop only, ticks 1-600 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 2.702 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.871 | 68% |
| WASM core, ticks 601-1200 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 5.458 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 3.225 | 41% |
| Native stress, 384 escorts/team, ticks 1-600 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 8.952 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 5.963 | 33% |

Run-mean ranges: native standard 1.695-1.712 -> 1.006-1.025 ms; WASM standard 10.105-10.241 -> 6.020-6.083 ms; native stress 8.490-9.658 -> 5.926-6.021 ms. These are observed effects, not a formal significance test. Native standard hit processing drops from 0.955 to 0.442 ms and lifecycle from 0.216 to 0.038 ms. AI does not get cheaper in every small-population window: index construction has overhead.

Median browser core P95 falls from 16.0 to 10.7 ms, but observed optimized maxima remain 17.7-18.2 ms. Rendering was excluded: this is not a guarantee that every complete frame stays within the 16.67 ms budget.

In the browser's first 600 ticks, 736,902 of 790,785 sweeps (93.2%) are rejected without narrow-phase rays/point queries; in ticks 601-1200, 1,045,753 of 1,054,317 (99.2%). This is skipped work, not skipped damage: before/after gameplay checksums and counters agree.

Three independent fresh runs per build/scenario, separate 600-tick warmup, seed 7331, particles enabled. Revision order alternates. Tables use the median of run means, not the fastest tick or fastest run. Native `Step` excludes rendering/capture. Browser `Advance(1)` includes binary frame export but Canvas/DOM are paused; it is real Mono AOT, not the interpreter. Compilation and timed runs do not overlap. Background load and scheduling still introduce noise, especially in the stress scenario.

Native uses .NET 8.0.31 CoreCLR/Release; web uses SDK 8.0.425, .NET 8.0.31 Mono AOT/LLVM `-O2`, Chromium headless, 1440x900. Standard population starts with 96 escorts and three carriers per team; stress uses 384 escorts per team. The stress population declines during combat, so these are replay-window averages, not constant-population throughput guarantees.

Within each runtime, before/after checksums, counters and populations agree. Native and WASM are not asserted bitwise-identical to each other; their timing ratio is not pure compiler overhead. This does not replace the original long-running five-framework comparison.

## Validation And Reproduction

- 30,203 differential swept queries: random short/long/stationary paths, compound/circular fixtures, movement, rotation, disabled bodies, zero radius, grazing paths, serial ties and identical impact coordinates.
- Finalized cached frames equal full ECS captures through combat churn. External transient creation/expiration remains visible through the default API.
- Grid boundaries, negative coordinates, conservative candidates, stable squad-order ties, reverse threats, recent-attacker priority, docking, cover and cache invalidation are checked. Warmed grid operations allocate no new buffers.
- All five adapters check mount reparenting/death and competing dock arrivals with same-tick undocking/destruction, in both serial orders.
- All five adapters reproduce the original ten native checkpoints across 3,000 ticks, ending at `DC7DFAFE4769F423` for 16 escorts/team and seed 27. Gameplay, armament and ECS validation suites also pass.

```powershell
dotnet build SpaceBattle.Headless/SpaceBattle.Headless.csproj -c Release --artifacts-path Benchmarks.Local/tmp/spatial-verified
dotnet Benchmarks.Local/tmp/spatial-verified/bin/SpaceBattle.Headless/release/SpaceBattle.Headless.dll --validate
dotnet Benchmarks.Local/tmp/spatial-verified/bin/SpaceBattle.Headless/release/SpaceBattle.Headless.dll --validate-ports
node --test tools/space-battle/test_summarize_spatial.mjs
```

The [browser profiler](../../tools/web/profile-revisions.mjs) accepts `--sites` for two local published roots and closes its localhost servers afterwards. The [summary generator](../../tools/space-battle/summarize-spatial.mjs) refuses to summarize changed gameplay trajectories. Builds/raw scratch outputs remain under ignored `Benchmarks.Local/tmp`; the separate [summary evidence](spatial-optimization/summary.json) records independent means, ranges, native stages, counters and binary hashes.

## Remaining Costs

Global kill-cost target selection still scans all enemies on decision ticks. Its worst case remains `O(S^2)` across actors. The current rules allow pursuing distant reachable targets; replacing this with an arbitrary radius would change AI behavior. Safe further pruning needs a proved lower bound on the kill-cost score, not a nearest-only shortcut.

Dense grid neighborhoods can still contain the whole fleet, and real overlapping physics contacts can require quadratic candidate work. A broadphase reduces irrelevant work, not the cost of genuinely interacting pairs. Ship collection still runs at tick boundaries for compatibility with external Store edits. Snapshot packing, rendering and validated ECS access remain separate costs; eliminating them was not claimed here.
