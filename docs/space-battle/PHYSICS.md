English | [Русский](PHYSICS.ru.md)

# Circle Physics Experiment

[Desktop](../../SpaceBattle.Game/README.md) | [Web](../../SpaceBattle.Web/README.md) | [Local comparison tool](../../tools/physics/README.md)

The shared C# simulation now uses `circle-kernel-v1`, not Aether. It is the same
implementation in desktop and production Mono AOT WASM. AI, guns, artwork and
the preceding LightSwarm changes are retained. Historical benchmark measurements,
captures and reports still describe their original Aether workload and are not
new measurements of this model.

## Observed Baseline

The preserved [Aether wrapper](../../tools/physics/AetherReference.cs) used game
units scaled by 0.1. Interceptor/Fighter had one circle; the other classes had
4-6 overlapping fixtures: a fuselage rectangle, two wing rectangles, a round
core, and optional bow/stern circles. Fixture densities summed to the requested
mass; overlapping areas counted in that sum. Friction was zero, restitution 0.1,
with no engine linear/angular damping.

Steering already changes the persistent `Motion.AngularVelocity` using a bounded
turn acceleration. Physics integrates the angle and returns collision-adjusted
linear/angular velocities. Off-center compound-shape normal impulses could add
rotation even with zero friction. Gun muzzle velocities also include the parent
ship's rotational point velocity. None of those AI or gun calculations is removed.

## Dynamics

[PhysicsService](../../SpaceBattle.Core/PhysicsService.cs) stores one homogeneous
disk per ship: position, angle, linear/angular velocity, radius, mass and inertia
`I = 0.5 * mass * radius^2`. Normal circle impulses are radial and deliberately
do not create spin. Configurable Coulomb tangential friction uses a shared contact
point, contact-point velocity and the correct arm/inverse-inertia denominator.
It can exchange translational/rotational motion; there is no random spin.

[Settings](../../SpaceBattle.Core/PhysicsSettings.cs) are available through
`BattleConfig.Physics`. Defaults: restitution 0.1, friction 0.08, restitution
speed threshold 10 game units/s, eight velocity and eight positional iterations,
0.01-unit positional slop, 0.8 correction fraction and 0.5-unit maximum relative
correction per pair/iteration. Friction 0 recovers frictionless disk contacts,
not the old compound geometry. These settings are initial tuning, not a claim
of equivalent trajectories.

Velocity passes repeat over the complete coupled contact set when projection
introduces a new pair; resolving only that new pair would invalidate neighboring
constraints. Such rounds are bounded by the number of positional iterations.

Adaptive substeps limit ordinary ship travel to approximately half a radius;
the hard ceiling is eight. Extremely fast ship motion can still tunnel above
that ceiling: ships do not have an unbounded global time-of-impact solver.
Linear/angular velocities persist between steps without additional engine drag.
The existing steering damping remains outside the kernel.

Positional projection is inverse-mass weighted and changes positions only;
it does not inject corrective velocities or damage impulses. Unique begin-contact
events use canonical serial pairs and pre-solve point speed. The existing gameplay
rule remains: speed below 10 does no collision damage; above it, each hull receives
`(speed - 10) * 0.35` through the normal shield/armor path. Persistent contact is
not reported as a fresh impact every iteration. Docked/disabled ships are omitted
from contact, projectile and placement queries. Decorative particles and turrets
do not get ship bodies.

## Broadphase And CCD

[The grid](../../SpaceBattle.Core/SpatialBoundsIndex.cs) inserts whole swept AABBs,
not just centers. It includes different radii, negative coordinates and cell
boundaries; query stamps deduplicate IDs. Large bounds use a bounded-membership
fallback, and long queries scan occupied cells rather than billions of empty ones.
Contact discovery is local; solver ordering is canonical by ship serial.

Targets retain every recorded substep endpoint. Projectile candidates use the
union of these target paths, while narrow tests map the projectile's tick segment
into each corresponding time interval. A target's excursion outside its final
cell is therefore not lost. The recorded path is piecewise linear between solver
outputs, including positional projection; it is not a reconstruction of an exact
continuous rigid-body trajectory through an instantaneous correction.

[Analytic tests](../../SpaceBattle.Core/CircleSweepMath.cs) replace five offset rays:
disks solve relative segment vs the sum of radii; MachineGun/Cannon rounds use a
fixed-orientation rectangle with half-length `2 * Radius` and half-width `Radius`.
The rectangle-circle sum is exactly a rounded rectangle (side strips and corner
arcs), not a capsule or expanded box. Plasma, missiles, heavy rounds and fragments
use disks. The long raster trail is decorative, not physical length. Rectangle
heading is set before the tick's linear translation; there is no rotating-rectangle
sweep. Both shapes select earliest time, then lower serial for exactly equal times,
including tangency, start overlap and zero motion. Calculations use double
intermediates and a stable quadratic form; float positions still define the state.

Owner immunity remains ship-specific, including the parent of a turret. Teams do
not provide immunity; other allies/enemies can be hit. Sweeps run before
out-of-arena retirement so a fast round crossing a target and leaving the map
within the same tick does not silently skip that hit.

## Resource And Accuracy Limits

Reserved grid arrays and query buffers avoid per-query/per-step allocation after
body reservation. Contact buffers reserve all possible pairs up to a configurable
32,768-pair ceiling at ship creation, not inside the solver. Exceeding the explicit
per-phase/per-tick contact budget fails with a diagnostic instead of silently
dropping collisions or allocating unbounded memory. Spawn/removal/setup and
measurement serialization are not zero-allocation claims.

Dense real overlap still has an `O(N^2)` worst case; spatial partitioning only
removes unrelated candidates. Limited iterations can leave temporary penetration
in a pile, resolved over subsequent steps. Projection is not energy/momentum
conserving motion; conservation tests use contacts below the correction slop.
Replacing compound hulls with circles changes silhouette contacts, grazing hits,
spin, survivorship, waves and checksums. Fixed seed/order is verified within each
runtime; native/WASM bitwise determinism is not claimed.

## Verification And Measurements

[Kernel tests](../../SpaceBattle.Headless/PhysicsKernelValidation.cs) cover masses,
elastic momentum/energy, angular integration, friction torque/angular momentum,
contact lifecycle, docking, correction without explosions, dense stability and
reserved allocation. [Geometry tests](../../SpaceBattle.Headless/AnalyticSweepValidation.cs)
include 12,000 independent distance-oracle sweeps and 3,000 brute-force bounds
queries. Existing mechanics, owner immunity, warp, armament and all-ECS replay
checks run against the new model. Old Aether checksums are intentionally not a
golden reference for changed physics.

Reproducible native and AOT comparison commands, timing scopes, independent
repeats and known instrumentation overhead are in the [study tool](../../tools/physics/README.md).
Raw results go only into ignored `Benchmarks.Local/tmp/physics-study/`; this
experiment does not overwrite the published benchmark tables.

## Measured Experiment (2026-10-12)

The [machine-readable summary](physics-experiment-summary.json) retains every
independent run mean, ranges, input/raw/binary hashes, workload counts and
checksums. Machine: Ryzen 9 9900X, 24 logical CPUs, Windows build 26200.
Builds and other QA runs were finished before each timing window; existing
user applications/previews were left alone. This is local evidence, not an
isolated laboratory or a statistical significance claim. Values below are
milliseconds, median of three independent run means, not the best tick/run.

### Native Prescribed Scenes

.NET 8.0.31 x64, tiered compilation disabled, seed 7331, 120 warmup ticks in a
separate world, 120 measured ticks per fresh world, 1,024 sweeps per tick.
Outer passes disable the replacement's internal timing. Total includes prescribed
state resets; Step and Hits are narrower scopes. Each slash means Aether / circles.

| Scene | Aether Total | Circle Total | Step A / C | Hits A / C |
|---|---:|---:|---:|---:|
| 198-dense-round | 2.859 | 2.723 | 0.276 / 2.268 | 2.557 / 0.453 |
| 198-legacy-compound | 1.308 | 0.239 | 0.052 / 0.030 | 1.160 / 0.204 |
| 198-mixed-round | 0.849 | 0.241 | 0.020 / 0.031 | 0.809 / 0.206 |
| 198-moving-target-round | 0.856 | 0.448 | 0.025 / 0.190 | 0.819 / 0.256 |
| 198-sparse-round | 0.828 | 0.224 | 0.020 / 0.029 | 0.788 / 0.191 |
| 774-dense-round | 5.864 | 15.688 | 1.491 / 15.016 | 4.233 / 0.653 |
| 774-legacy-compound | 3.154 | 0.402 | 0.191 / 0.120 | 2.558 / 0.268 |
| 774-mixed-round | 1.595 | 0.429 | 0.083 / 0.130 | 1.427 / 0.283 |
| 774-moving-target-round | 2.305 | 1.052 | 0.111 / 0.728 | 2.128 / 0.318 |
| 774-sparse-round | 1.668 | 0.407 | 0.080 / 0.119 | 1.498 / 0.271 |

Round scenes share collider geometry and prescribed inputs, not solver settings:
Aether retains zero friction/default iterations; circles use the settings above.
Compound rows intentionally change geometry. Moving-target rows preserve paths
and intentionally find additional crossing hits. Solver outputs do not become
the next tick's inputs. None of these rows is a historical gameplay replay.

The 774-body dense scene is an explicit regression: total is 2.68 times slower,
despite cheaper hits. Independent total means span 5.661-6.860 ms for Aether and
15.387-15.774 ms for circles. Repeated grid rebuilds/contact discovery and coupled
constraint passes dominate. The same density with 198 bodies has only a 4.7%
lower total, while its Step is still much more expensive. Spatial partitioning does not
remove true dense-pair complexity.

Separate instrumented passes for the 774-body dense scene:

| Phase | Aether | Circles |
|---|---:|---:|
| Contact search | 0.181 | 9.552 |
| Solver | 1.365 | 5.329 |
| Hit candidate rejection/search | 0.262 | 0.450 |
| Hit narrow path | 4.093 | 0.208 |

Aether's search/solver are its actual exported internal labels. Its hit narrow
path includes tree searches and five rays, not pure geometric arithmetic.
The pinned assembly's built-in diagnostics are constant-on, including outer
passes. The phase table is not an exhaustive/disjoint decomposition of total.
All three uninstrumented repeats in all ten circle scenes report zero managed
bytes inside the measured frame; setup/spawning is excluded.

### Production Mono AOT WASM

Chromium 151.0.7922.34; same seed 7331, LightSwarm initial fleet, three churn
profiles. Each fresh browser context uses 120 warmup ticks in a discarded battle,
then 600 measured ticks from zero. Rendering is paused during the synchronous
C# Advance loop; this is simulation timing, not FPS. Ranges in parentheses are
the minimum/maximum of independent run means.

| Profile | Aether Tick (range) | Circle Tick (range) | Physics stage A / C | Hits stage A / C | Ships at 600 A / C |
|---|---:|---:|---:|---:|---:|
| low | 5.620 (5.541-5.769) | 5.699 (5.575-5.875) | 1.050 / 1.963 | 1.564 / 0.724 | 75 / 78 |
| nominal | 6.616 (6.595-6.897) | 5.825 (5.505-5.872) | 1.151 / 1.615 | 1.951 / 0.969 | 88 / 48 |
| high | 8.529 (8.444-8.632) | 7.432 (7.239-7.814) | 1.346 / 1.648 | 2.737 / 1.502 | 145 / 130 |

Nominal/high observed total reductions are about 12%/13%; low is practically
unchanged with overlapping repeat ranges. New contact search is more expensive,
while hit processing is cheaper. Survivorship, shots and wave production differ
because geometry/CCD/friction changed. These are full-workload results, not an
isolated equal-state solver/compiler speedup, and only cover the first 600 ticks.

The old bridge exposes combined stages but no inner phases. New inner timings
come from additional independent instrumented contexts, never invented old zeros:

| Profile | Contact search | Solver | Hit candidates | Hit narrow | Instrumented tick |
|---|---:|---:|---:|---:|---:|
| low | 1.400 | 0.023 | 0.302 | 0.077 | 5.994 |
| nominal | 0.909 | 0.015 | 0.434 | 0.136 | 6.292 |
| high | 0.634 | 0.011 | 0.664 | 0.222 | 8.045 |

Physics/Hits gameplay stages also contain state transfers, damage, effects and
other work outside those inner timers. Instrumented totals include overhead;
use the outer table for throughput. Browser timer quantization limits tiny phases.
Within each runtime/profile all three outer repeats have identical checksums;
native/WASM or Aether/circle checksum equivalence is not asserted.

The exact loaded uncompressed native WASM resource shrank from 18,035,000 to
9,721,086 bytes (46.1%). This is not total transfer size or proof of speed; both
sides still use Mono AOT. Production deps/boot manifests contain no Aether.
Aether 2.2.0 remains in the isolated comparison project and the existing
historical composition-replay tool, with its license. Those are not runtime dependencies.

Local raw evidence: `Benchmarks.Local/tmp/physics-study/circle-native-final-02`
and `circle-web-final-02`. Failed/intermediate runs are retained as diagnostics,
not selected as the final result. Historical measured files and published tables
remain unchanged.

Solver background: [Erin Catto, Modeling and Solving Constraints](https://box2d.org/files/ErinCatto_ModelingAndSolvingConstraints_GDC2009.pdf).
The implementation is project-authored C#, not copied Box2D source.
