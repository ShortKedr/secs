English | [Русский](LOCALITY_OPTIMIZATION.ru.md)

# Portable Space Battle Locality

Local study, 2026-10-12. The baseline is the current dirty workspace, including
the earlier circle-kernel and SECS mapping changes, not the historical published
benchmark. Gameplay, balance, RNG, physics and AI decision rules are unchanged.
Historical measurements are not replaced. No remote operation was performed.

## Implementation

1. [Component-only passes](../../SpaceBattle.SECS/SecsStore.cs) avoid native
   entity construction and reverse mapping for projectile, debris and particle
   movement. Dense generation-aware mapping already existed in the baseline;
   there was no dictionary lookup on every ordinary SECS row. The other four
   adapters retain their original native query through a keyed fallback. This
   is not a claim of an equivalent mapping improvement in those adapters.
2. [Steering](../../SpaceBattle.Core/Battle.cs) lazily reuses actor identity,
   weapon, target liveness and position within one row. Docking and conditional
   heavy-weapon/overheat reads retain their early exits. The exact damping
   expression is cached by raw damping/dt bits in a bounded
   [64-slot table](../../SpaceBattle.Core/DampingFactorCache.cs). Nonfinite
   inputs bypass it; parameter changes and live `ref` edits cannot return an
   old coefficient. No approximate exponential is used.
3. [AI](../../SpaceBattle.Core/TacticalAi.cs) borrows nongeneric snapshots and
   builds ordered opponent-index lists lazily, only after an unlocked target
   search is required. At most two O(N) scans prepare the normal team lists.
   Nonstandard teams retain a complete scan. Lazy caches retain the original
   left-associated DPS arithmetic and target effective-health expression.
   Snapshot values never replace steering's live target-position reads.

The opponent lists do not impose a target radius, prune reachable targets or
change serial tie order. Global target scoring remains potentially O(S squared).
We did not split missiles into a new archetype/list: their owner/target checks
and the useful missile-only short circuit remain. No broad accessor caches,
SIMD kernels, unsafe indexing, or blanket aggressive compilation attributes
were added.

SECS chunk spans are limited to a guarded query. Its local `Count` bounds every
column; structural mutation and disposal are rejected until the query ends,
including nested callbacks. References are never retained across mutations.
We do not claim all JIT/AOT bounds checks disappeared. Focused IL tests verify
the selected AI methods no longer call `List<AiShipSnapshot>.get_Item` or copy
the whole snapshot with `ldobj`; this is not a claim that every native memory
copy or Mono generic trampoline has disappeared.

## Native Results

Windows 11, Ryzen 9 9900X, .NET SDK 8.0.425 / runtime 8.0.31, Release.
For this isolated throughput matrix, tiered compilation and ReadyToRun were
disabled equally in both variants. This is FullOpts native Step, not renderer
FPS or an ARM measurement. Seed 7331, 96 ships/team, LightSwarm, endless corner
reinforcements, carrier replacement disabled. Low/nominal/high use churn
multipliers .5/1/2. Each process warms a separate 2400-tick battle and measures
a fresh battle at ticks 1-2400, with 600-tick checkpoints. Three independent
repeats per stage/profile; stage order rotates, no overlapping builds or trials.

Values below are medians of independent run means; brackets are their ranges.
p95/p99 are medians of the per-run fixed-window percentiles, not pooled ticks.

| Profile | Before Step ms | Final Step ms | Observed reduction | Before p95 / p99 ms | Final p95 / p99 ms |
|---|---:|---:|---:|---:|---:|
| low | .4189 [.4163, .4432] | .4034 [.3984, .4147] | 3.7% | .7387 / 1.0077 | .7180 / .8995 |
| nominal | .5570 [.5559, .5595] | .5373 [.5333, .5428] | 3.5% | .9972 / 1.2415 | .9738 / 1.2425 |
| high | .6956 [.6930, .7196] | .6535 [.6286, .7197] | 6.0% | 1.2734 / 1.9038 | 1.1697 / 1.7747 |

| Native Stage | low Step ms | nominal Step ms | high Step ms |
|---|---:|---:|---:|
| Baseline | .4189 | .5570 | .6956 |
| Component-only movement | .4152 | .5499 | .6765 |
| Plus steering/damping | .4092 | .5260 | .6558 |
| Plus AI locality | .4034 | .5373 | .6535 |

The physics-labelled stage includes movement of transient objects, not just
the contact solver. Baseline/final stage means: physics .0823/.0742,
.0873/.0732, .0897/.0667 ms; steering .0304/.0220, .0283/.0207,
.0246/.0181 ms; AI .0457/.0443, .0462/.0444, .0420/.0389 ms.
The solver itself was not modified.

Small effects are noisy, not statistically established. High run ranges overlap.
The AI addition did not improve nominal total Step over the steering-only stage;
nominal p99 also did not improve over baseline. Export was not optimized:
baseline/final means .0798/.0767, .1033/.0991, .1695/.1803 ms.
The high export increase is retained as an observed limitation, not hidden.

Baseline/final bytes per measured tick: 1001.57/1005.32, 1744.13/1747.89,
2728.63/2732.39. The additional 9008 bytes over each 2400-tick replay are
new lazy buffers/growth, not a new per-row allocation. The warmed component
pass allocates zero bytes. The AI metadata costs approximately 45 extra payload
bytes per reserved snapshot slot (DPS, health, two index lists), plus array
headers/rounding. The damping table is about 1 KiB of payload per battle.
Heap telemetry varies with GC and is not evidence of lower peak process memory.

## Browser And Scalar Verification

Production Mono AOT still uses stable .NET 8 and the measured original
native-C -Oz / AOT-bitcode and link -O2 pipeline. No CoreCLR/R2R or fast-math.
The production project explicitly sets `WasmEnableSIMD=false`. A browser cannot
choose a fallback inside a SIMD module which it has already rejected at load
time. No two-module loader is introduced; the delivery is conservatively scalar.
The existing desktop runtime may still choose supported System.Numerics
instructions; a forced `DOTNET_EnableHWIntrinsic=0` run checks its fallback.

Browser result table and additional verification are recorded after the local
production comparisons finish; do not treat the native percentages as WASM gains.

## Reproduction And Limits

New raw data and source/build identities stay under ignored `Benchmarks.Local/tmp`:
`data-locality-20261012`, `data-locality-final-20261012`,
`locality-native-alternating-20261012`, `locality-browser-primary-20261012`.
They are intentionally not links to published historical benchmark data.

[locality-study.ps1](locality-study.ps1) runs source-stage native harnesses with
rotating order. [summarize_locality.py](summarize_locality.py) rejects different
configs, checksum, counters, tactics or population aggregates before comparing
performance. [The browser harness](../web/mono-compiler-study.mjs) uses fresh
processes, separate warmup/reset, exact per-tick state and checkpoints, and
separate Step/export/frontend CPU timing. Frontend timing is Canvas submission,
not GPU busy time, compositor cost or real-time FPS.

No branch/cache-miss reduction is asserted. Installed WPT advertises PMCs, but
this task has no administrative tracing token and did not modify any existing
tracing session. [Microsoft's PMU procedure](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/recording-pmu-events)
is a separate diagnostic protocol, not a counter measurement in this report.
Power, thermals, OS caches and other user processes remain uncontrolled.
Cross-platform bitwise determinism and untested CPU/browser architectures are
not claimed.
