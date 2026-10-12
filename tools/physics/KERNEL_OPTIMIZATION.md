English | [Русский](KERNEL_OPTIMIZATION.ru.md)

# Circle Kernel Structural Study

This harness compares the current circle kernel with preserved circle-kernel
assemblies, not Aether. Historical physics results and published reports are not
inputs or outputs. Run from the repository root. All new raw artifacts must be
under a fresh ignored `Benchmarks.Local/tmp` directory. No remote operations are
used. Build first and finish every build/test workload before opening a timing
window. Do not stop unrelated processes.

## Reproduction

Build `KernelBench.csproj` in Release. `-p:StudyAssemblies=ABSOLUTE_DLL_DIRECTORY`
binds the harness to preserved `SpaceBattle.Core.dll` without rebuilding it.
Archive the actual dirty source and baseline binaries before editing production
code; the execution-time source archive is not proof of a baseline DLL's source.
The harness hashes the actual assemblies.

```powershell
dotnet build tools/physics/KernelBench.csproj -c Release
python -B tools/physics/kernel_bench.py run --baseline BASELINE_KernelBench.dll --candidate CANDIDATE_KernelBench.dll --output Benchmarks.Local/tmp/NEW_PAIR --approved-timing-window --repeats 3 --frames 120 --sweeps 1024
python -B tools/physics/test_kernel_bench.py
```

Record runtime/environment settings explicitly. Run default desktop separately
from native FullOpts (`DOTNET_TieredCompilation=0`) microbenchmarks. Each paired
repeat uses a fresh process, alternates revision order, warms the same complete
input sequence separately, then resets contact history without releasing reserved
capacity. Inputs and serialization are outside Step/hit timing. SHA256 includes
the float bits of every exposed position, angle, velocity, spin, contact and
circle/rectangle hit or miss impact, with disabled bodies and owner exclusions.
There is no cross-platform bitwise-determinism claim.

Use a separate `--instrumented` pair for `CollectTimings` and `CollectDiagnostics`.
Never present its clocks as ordinary throughput. Counters reset at Step and include
subsequent CCD/spawn queries until the next Step. Contact builds are logical passes;
grid rebuilds count Seal calls. Membership visits include the exact-bounds fallback;
unique candidates are per query, not per frame. ContactPairs counts successful
narrow checks, NewConstraints counts unique substep constraints. Repeated velocity
passes still execute the entire constraint list, with unchanged iteration counts.

Cell/margin probes use candidate-only `--cell-size` / `--margin` controls, leaving
the physical input file identical. Compare at least three independently warmed
repeats across both body counts and densities, not just one favorable scene. p95
and p99 are nearest-rank tick percentiles *within each repeat*, not independent
trials. The principal estimate is the median of independent repeat means; retain
their range, all tail percentiles, and paired ratios. Short probes cannot establish
tail significance. Process memory includes the input package and observer; only
the separately delimited Step/hit allocation counters describe the hot path.

## Preserved Model

The shared C# solver retains the circle geometry, masses/inertias, restitution,
friction, integration, substeps, positional iterations and full velocity passes.
Tangents and mass denominators are invariant because the original constraint
normal/arms were already fixed at creation. They are cached with their original
float/double operations, not reciprocals or reassociation. Live velocities and
angular velocities are always recomputed. No warm starting is added.

The candidate cache stores serial-canonical pairs of overlapping fat bounds.
Before reuse every enabled body's original outward-rounded tight bound must lie
inside its stored fat bound. Any exit rebuilds the entire conservative set. Every
remaining pair is retested against the original tight AABBs and exact original
circle predicate; successful pairs are compacted out without reordering survivors.
New constraints are appended in the same order as a complete per-body rebuild.
This discovers new neighbors after correction without changing constraint normals,
contact-event timing or numerical response. Out-of-window / oversized bounds keep
the exact fallback. Pair budget overflow switches to a complete uncached path for
that substep, never truncates. `CacheContactCandidates=false` is a reference/control.

Body IDs, not global effect serials, index dense query marks. The generic spatial
index keeps duplicate-ID behavior. Contact queries sort once by serial; CCD keeps
numeric output ordering and its serial tie-break. Shared grid defaults remain
48 world units until multi-scene measurements justify a change. The 128x128 cell
metadata window is bounded; membership arrays retain the conservative 64 entries
per reserved bounds object so admissible redistributions do not allocate during
Step. The new pair buffer is capped by MaximumCandidatePairs (262144 by default,
2 MiB of pair payload); it never grows on a hot query or correction.
The default fat margin is 4 world units: paired multi-scene probes compared 2, 4
and 8. The shared cell size stays 48 after comparisons with 24, 32 and 64 showed
trade-offs rather than a consistent improvement. Probe raw files belong in the
ignored study directory, not this methodological document.

Rectangle CCD prepares double Sin/Cos once per shot, retaining the exact rounded
rectangle Minkowski geometry. Relative displacement arithmetic stays in double
with unchanged expression order. An interval is skipped only when its start time
is *strictly* later than the current earliest hit; equality remains eligible for
serial ties. Moving-target paths, start-inside, zero movement, thickness, owner
immunity, neutral team/debris damage and decorative-particle exclusion remain.
Common displacement and trigonometry are prepared lazily on the first eligible
body: empty/owner-only/disabled-only queries do not perform this arithmetic. The
prepared overload contains the geometry directly, avoiding an extra hot wrapper.
First intervals always remain eligible without a needless zero-start division.

## Remaining Cost

Connected-component pruning needs a proof of numerical fixed point, not merely
an unchanged graph. An existing component can still have residual impulse updates
after eight iterations. Skipping it when an unrelated new pair triggers another
pass would change the original solver result. This optimization therefore keeps
all passes, avoiding a hidden numerical-model change. A future experiment would
need exact component-state fixed-point detection and preserved interleaving, then
separate replay and cost evidence.
The validation suite demonstrates residual velocity changes after an extra full
pass on an unchanged coupled graph; graph identity alone is not a safe criterion.

Genuinely dense contacts still have quadratic pair count. Spatial partitioning
cannot remove physical constraints between all overlapping bodies. Fat envelopes
may increase candidate count, escaping them forces rebuilds, and exact fallback
queries outside the bounded grid can also become quadratic. Budget fallback trades
speed for bounded memory and correctness. Synthetic prescribed-state scenes force
density anew each tick and are not ordinary gameplay or historical Aether scenes.
Production AOT game runs, long endless replays, export and renderer timings must
be reported separately; screenshots alone do not prove contact or CCD correctness.
