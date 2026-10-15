English | [Русский](HARDWARE_OPTIMIZATION.ru.md)

# Behavior-Preserving Hardware Optimization

<!-- research-colors:start -->
### Colour scale

![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") up to 5% · ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 15% · ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") from 30% · ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") neutral.

Colour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules](../benchmarks/COLORS.md).


Only selected metrics within one scenario/window/environment are compared. Measured comparisons stay grey when neither the table nor its linked summary supplies independent-repeat envelopes. Stage shares and gameplay counters are not colored.
<!-- research-colors:end -->

Local study, 2026-10-12. This is not a replacement for frozen five-ECS measurements. [Harness and reproduction protocol](../../tools/HardwareStudy/README.md).

## Protocol

The study uses the nominal gameplay scenario: seed 7331, 96 escorts per team, LightSwarm, endless reinforcements from the corners and no carrier replacement. Circle physics, armament and tactics revisions are unchanged between variants. Native Release .NET 8.0.31 and Chromium 151 Mono AOT results are compared separately.

Native measurements use a separate 1200-tick warmup and three independent fresh 1200-tick replays per stage and port. Browser measurements use a separate 600-tick warmup and three fresh replays, alternating version order. Each replay is divided into four 300-tick windows. Tables report the median of independent run means, followed by their [minimum, maximum]. Individual ticks are not independent repeats. Background desktop activity and tiered compilation can affect the results; no statistical significance is claimed.

After each window, native checksums, every gameplay/AI counter and mean/end populations must match across all five ports. The browser comparison checks the checkpoint, exported gameplay/AI counters and all five populations. Checksums are not compared between native and browser runtimes.

Five-repeat Uniform/7331 replays and the additional seed 27/low and seed 42/high cases provide separate regression evidence. They do not replace measurements of the nominal gameplay scenario.

## Stages And Costs

| Stage | Implemented change | Cost or retained limitation |
| --- | --- | --- |
| 1 | Remove unused mounted-shot Predict; phase-local parent reads and Sin/Cos cache; re-fetch Heat after structural creation | Two turret synchronization phases remain separate; lazy reads preserve gates. Small total effect can be noise. |
| 2 | Flat normalized x+y*width cells, sealed Start/Count shared buffer; append chains for interleaved leaders | Default 256 KiB metadata/grid, cap 1 MiB/grid. Member storage 12 bytes/membership, up to 64/bounds entry; entry capacity follows caller/entity limits. Exact out-of-window fallback is O(N). |
| 3/4 | Snapshot membership marks, flat inline Squad, generation-aware EntityKey.Id tables, ref-readonly/in snapshots and lazy scoring invariants | Nongeneric data for Mono AOT. Dense AI payload cap 12 MiB/table plus sparse fallback; reserve 2N squads before borrowing refs. Snapshot and live reads remain distinct. |
| 5 | Dense native-ID/version query-to-public mapping; cache repeated Heat/Transform reads | Reverse payload cap 768 KiB, dictionary fallback. Public stale/disposal guards and native registration/removal dictionary remain. No cached component ref across storage mutation. |
| 8 | Profiled bounds and a narrow mapping hot/cold split; split rejected | Integer-only experiment, no unsafe/pinning. Optimized native CellQuery disassembly still contains a range-check failure path; branch misses were not measured. |

No float reassociation, explicit FMA or fast-math was introduced. Candidate sorting and strict ties remain. Global TargetCost still scans distant reachable targets and can be O(S squared). Dense contact/query workloads can also remain quadratic; a grid does not remove that worst case.

## Measured Stage Matrix

Initial window 1-300, Step ms. Stage 5 is the retained production implementation; the later split experiment is not included in it. Detailed [native stages](hardware-optimization/native-stages.json), [Mono stages](hardware-optimization/mono-stages.json), [rejected split](hardware-optimization/mono-rejected-split.json) and [store probes](hardware-optimization/store-probes.json) retain all comparison groups.

| Stage | Native SECS | Mono AOT |
| --- | ---: | ---: |
| Before | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.999 [0.961,1.199] | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 7.765 [7.662,8.053] |
| 1 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0.902 [0.889,0.937] | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 7.742 [7.696,7.778] |
| 2 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0.849 [0.821,0.925] | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 6.750 [6.747,6.799] |
| 3/4 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0.878 [0.808,0.889] | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 6.738 [6.716,8.248] |
| 5 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.725 [0.723,0.909] | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 5.028 [5.008,6.239] |

At stage 5, window 901-1200: native 0.761 → 0.604 ms; Mono 5.870 → 3.992 ms. Initial Mono physics stage 2.335 → 0.884 ms and collect 2.457 → 1.734 ms. AI stage 2 → 3/4: 0.494 → 0.397 ms, while total Step overlaps. Native phase-end Capture(false) 0.229 → 0.243 ms; Mono export 0.730 → 0.705 ms: no demonstrated export improvement.

Opt-in physics clocks are measured in a separate equally instrumented three-run series, ticks 1-300. Mono contact candidates 1.397 → 0.652 ms, hit candidates 0.423 → 0.260 ms, solver 0.024 → 0.023 ms, hit narrow 0.112 → 0.107 ms. Native corresponding medians 0.174 → 0.142, 0.064 → 0.061, 0.011 → 0.009, 0.017 → 0.018 ms, with overlapping ranges and a native contact-candidate outlier 0.374 ms. Do not subtract these instrumented values from ordinary Step.

## Memory And Frontend

Stage 5 native first-window Step allocations 2,198,936 → 2,687,960 bytes and managed heap 7,355,432 → 8,209,416 bytes. These include fresh-battle table growth, not warmed-kernel allocation claims. Native ECS storage at tick 300 remains 2,989,057 bytes, 2461 entities, 41 archetypes, 64 chunks, 0 accessor bytes. Mono telemetry-managed heap approximately 21.2 → 22.9 MB; retained linear memory 100,663,296 → 100,859,904 bytes. Telemetry is sampled, not an exact managed allocator census.

Native frontend uses identical host/render/assets and swaps preserved simulation DLLs: three fresh uncapped 300-frame replays, 120 warmup frames, measured ticks 121-420. Default tiered series at stage 5: simulation 1.782 → 1.960 ms, total CPU frame 3.555 → 3.899 ms. This short-startup series does not establish a desktop win. Same-window separate FullOpts diagnostic (TieredCompilation=0, not a production change): simulation 0.882 → 0.760 ms, snapshot 0.144 → 0.144 ms, render submission 0.608 → 0.606 ms, total frame 2.189 → 2.066 ms. Experimental hot/cold mapping gave default simulation 1.824 ms and total frame 3.629 ms with overlapping baseline ranges. The split was rejected: its additional paired Mono series showed stage 5 → split 4.880 → 5.131 ms in window 1-300 and 1.996 → 2.367 ms in window 301-600, with wide ranges. No production tiering settings changed.

Canvas draw-only CPU submission at the same paused tick 300: before 5.369 ms [5.066, 5.439], after 5.192 ms [5.148, 5.208], three independent pages with 60 draw warmups/120 samples. Original app.js is unchanged; temporary diagnostic closure exposure excludes Step/export/DOM. No render improvement or GPU utilization claim. All six native GL profiles passed; checkpoint EEFA64DD84CED6DD/populations match.

## Historical CoreCLR Measurements

The experimental browser CoreCLR/R2R route has been retired and its host/tools removed. The paragraph and JSON below are retained unchanged as historical measurements, not a supported reproduction route. Production browser measurements use stable .NET 8 Mono AOT.

Pinned .NET 11 RC1 crossgen2 partial R2R builds run in real Chromium. Three independent 120-tick replays after 120-tick warmup: ticks 1-60 Step 112.048 ms [109.488,123.055] → 88.062 ms [87.272, 89.478]; 61-120: 159.483 ms [158.798,172.423] → 130.892 ms [129.452,132.338]. Checkpoints/counters/populations match. CPU sampling on 120 ticks finds interpreter leaf samples 84.927% before and 82.179% at stage 5. This is mixed execution, not a compiled-hot-path speed upgrade; longer windows and interpreter-free R2R remain unverified. [Separate R2R measurements](hardware-optimization/r2r-stage5.json).

A separate ComponentAccessor microprobe measured direct reads at 1.410 → 0.365 ms, but staged create/destroy at 9.322 → 10.669 ms, with an extra 49,152 accessor bytes at 4096 slots. The API was not adopted in production.

The final-archetype creation microprobe measured 4.158 ms versus 9.322 ms for staged creation. Code inspection shows birth row insertions falling from 4 to 1 and removals from 3 to 0; gameplay component additions remain 3. Copy bytes and reference repairs after swap-removal were not instrumented. Typed creation and query-order equivalence across all five ports are not proven, so gameplay creation and counters were not changed.

The current full Collect/Sort is retained: external additions/removals and Serial/Transform changes through returned refs remain visible. Tests cover five-port external edits, slot/native-ID reuse/divergence, epoch wrap/fallback, stale/disposal guards, exact grid oracles, ties, snapshot/live separation, self exclusion and friendly/debris damage. 16 SECS tests and 6 comparison tests pass. Native/browser battle images and real frontend controls were inspected; no assets or frozen reports changed by this work.
