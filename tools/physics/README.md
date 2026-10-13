English | [Русский](README.ru.md)

# Local Physics Study

[Research index](../../docs/space-battle/RESEARCH.md) | [Current campaign](../../docs/space-battle/REPORT.md)

Standalone native comparison tooling, not a runtime physics implementation.
`AetherReference.cs` contains the complete preserved pre-experiment wrapper in
`SpaceBattle.PhysicsStudy`; no private baseline path is required. Aether 2.2.0 is
pinned in this tool project. Production source is referenced, never modified here.
No results or performance claims are published by this harness.

## Build And Verify

Run from the repository root with the .NET 8 SDK. Restore/build before the approved
timing window; never use `dotnet run` for measurement.

```powershell
dotnet build tools/physics/PhysicsStudy.csproj -c Release --ignore-failed-sources
dotnet tools/physics/bin/Release/net8.0/PhysicsStudy.dll --inspect
dotnet tools/physics/bin/Release/net8.0/PhysicsStudy.dll --verify-baseline
dotnet tools/physics/bin/Release/net8.0/PhysicsStudy.dll --verify
```

`--inspect` prints exported Aether diagnostics and candidate API availability.
`--verify-baseline` checks seed reproducibility and reference hit/lifetime behavior
without needing the replacement API. `--verify` additionally checks candidate
settings/timings/query counters, circle hits and the rectangle sweep entry point.
Neither verification command is a timed benchmark.

The candidate must expose `PhysicsService(PhysicsSettings? settings = null)`,
`PhysicsSettings.CollectTimings`, `Timings` with `ContactCandidatesMs`, `SolverMs`,
`HitCandidatesMs`, `HitNarrowMs`, and `QueryStatistics.Candidates/NarrowTests`.
Existing `AddShip`, `SetShip`, `Step`, `Sweep`, `Bodies`, `Fixtures`, `Contacts`
remain in use. `GetMassProperties` and
`SweepRectangle(from,to,halfExtents,angle,ignoredSerial,out serial,out impact)`
are checked; rectangle sweeps are not substituted for baseline disk sweeps.
Reflection binds evolving settings outside measured loops; physics calls in the
loops are direct. Missing API fails explicitly instead of benchmarking old-vs-old.

## Timing Window

Reserve an idle timing window after all builds and other measurement workloads
have finished. The required flag acknowledges this precondition; it cannot detect
overlapping builds or other CPU activity. No browser, AOT preview, host or server is restarted.

```powershell
dotnet tools/physics/bin/Release/net8.0/PhysicsStudy.dll --approved-timing-window --run local-01 --seed 7331 --ticks 120 --warmup 20 --sweeps 1024 --repeats 3
```

For repeatable native diagnostics, use `DOTNET_TieredCompilation=0` with
`--warmup 120`, the protocol chosen after cold tier transitions in the initial
study. Build before the window, then set the environment for the
DLL invocation (restore the previous value afterwards in your shell):

```powershell
$env:DOTNET_TieredCompilation = "0"
dotnet tools/physics/bin/Release/net8.0/PhysicsStudy.dll --approved-timing-window --run circle-native-final-01 --seed 7331 --ticks 120 --warmup 120 --sweeps 1024 --repeats 3
```

The manifest records `DOTNET_TieredCompilation`, `DOTNET_TieredPGO`,
`DOTNET_TC_QuickJit`, `DOTNET_TC_QuickJitForLoops`, and `DOTNET_ReadyToRun` exactly as
provided; `null` means unset, not disabled. Existing `circle-native-01` is retained
as diagnostic evidence, not a final-effect result. At least three fresh repeats
remain required. This configuration does not detect whether the machine is idle.

All generated inputs, source/binary snapshots, manifest, per-frame raw results and
comparisons go exclusively under ignored
`Benchmarks.Local/tmp/physics-study/<run>/`. Existing run directories are rejected.
No deletes, Git commands, uploads or publication are performed. Record external
CPU conditions with the run when interpreting results.

## Prescribed Inputs

Ten synthetic scenarios use historical *counts* 198 and 774, not a historical
match replay: sparse equal-radius circles, sparse mixed-radius circles, legacy
4/5/6-fixture compounds, dense mixed-radius circles, and moving-target circles.
Round-only cases have equal collider geometry. Legacy compounds intentionally do
not: the replacement uses circles. The seeded xorshift generator and serialized
inputs/SHA-256 define exactly the same per-tick state and 1,000+ sweep queries for
both engines. Queries include zero-length paths, zero radius and ignored serials.
Moving probes are counted separately; their results are intentionally permitted
to differ because only the replacement accounts for relative target motion.

Each tick restores prescribed transforms and velocities with `SetShip` before
`Step`. In non-moving-target scenes, after observing step contacts, it sets identical prescribed kinematic end
states (`end = start + velocity * dt`, `Previous = start`) before hit
queries so solver displacement cannot contaminate query comparisons. This tests
static end-geometry queries independently of solver response, not a coupled
gameplay trajectory. The isolated moving-target scene preserves Step's motion paths:
`SetShip` would erase them. Its targets have identical velocities and large spacing;
any contact is rejected so solver response cannot bias those paths. `--verify`
checks a crossing hit observed by relative CCD but missed by the static Aether ray.
Moving targets use radius 4 and speed 1080 (18 units/tick), below Aether's
per-step translation cap; verification checks both endpoints before testing hits.
Step responses never become next-frame inputs. Reference
friction stays its historical zero and restitution 0.1; candidate defaults are
recorded, including its 0.08 friction. Geometry equality does not imply identical
solver settings or contact semantics. Contacts count unique *begin* events, not
all persistent Aether contacts.

Every engine/mode has a separate disposable warmup world, then at least three
fresh independently populated worlds. Engine order alternates by repeat. Untimed
input generation, warmup, forced GC, setup, serialization and I/O are excluded.
Within-engine hit/contact signatures must remain stable across repeats and timing
modes; failures preserve raw evidence. Cross-engine hit differences are recorded,
not hidden or asserted away. `TotalMs` includes both state resets, step and queries;
`CombinedStepMs` and `HitsOuterMs` are narrower outer measurements. Counter snapshots
and phase reflection occur after the outer timer. Allocation counts exclude snapshots.

## Honest Phases

`CollectTimings=false` passes measure outer totals without internal timestamp
instrumentation. Separate `true` passes provide phases; comparison files include
both total medians to expose overhead. Prefer uninstrumented totals for throughput.

The pinned 2.2.0 net8.0 assembly exports all seven `TimeSpan` properties and has
`EnableDiagnostics` compiled as **constant true**. Its internal timestamp overhead
therefore remains in both modes; wrapper-only overhead can still be compared.
For other builds, a writable `Settings.EnableDiagnostics` is enabled only in instrumented
worlds and restored on disposal; worlds must run serially because the switch is global.
If the package compiles it as a constant/read-only false value, enabling it is not
possible: search/solver internals are `null`, regardless of exported properties.
`--inspect` reports the actual switch value and mutability. A constant true value
cannot be disabled either; its built-in instrumentation remains in outer-only runs.
Reflection reads public `TimeSpan` diagnostic properties:

- Contact search: `NewContactsTime + ContactsUpdateTime`.
- Solver internals: `SolveUpdateTime + ContinuousPhysicsTime`.
- Outer wrapper: `CombinedStepMs` around `World.Step`.
- Hit candidates: wrapper empty-sweep `QueryAABB` rejection, including bounds setup.
- Hit narrow: the complete point-query/five-ray path, including its internal tree search.

These are actual Aether internal timer labels, not an exhaustive disjoint partition
of outer step time. Continuous solving may include further search; add/remove,
controllers and wrapper work are excluded from the two sums. Do not sum phases
and treat them as outer total, or call wrapper ray time pure narrowphase.
Disabled/missing/non-`TimeSpan` diagnostics become JSON `null`; `combinedStep` remains the
only step measurement if the split is unavailable. No zero-filled fake split.
Aether `Candidates` counts visited empty-rejection fixtures (short-circuits on the
first eligible fixture); `NarrowTests` counts explicit `TestPoint` calls only.
Internal ray narrow tests are not exported and are **not** invented. `Rays` and
`PointQueries` retain their original meaning. These counters are not interchangeable
with replacement engine pair/candidate counts.

Full gameplay, exact compound equivalence and rectangle baseline equivalence are
outside the native study.

## Production WASM Comparison

`tools/web/physics-study.mjs` compares a preserved Aether + LightSwarm site
with a separately prepared circle-kernel production AOT site. Ports 8049/8050
are example endpoints from the original study, not guaranteed running servers.
Prepare and verify both sites, then finish compilation and competing workloads
before timing; this script starts no builds or servers.

```powershell
node --check tools/web/physics-study.mjs
node tools/web/physics-study.mjs --verify
node tools/web/physics-study.mjs --validate-assets-live --run web-capture-only-01
node tools/web/physics-study.mjs --approved-timing-window --run web-local-01 --before http://127.0.0.1:8049/ --after http://127.0.0.1:8050/ --profiles low,nominal,high --seed 7331 --repeats 3 --warmup-ticks 120 --ticks 600
```

The first two commands perform syntax/configuration checks without opening a
browser, touching either site or running timings. The measured command requires
both `Mode() == aot-binary` and an observed native WASM resource. It records served
WASM/assembly/JS/config hashes and runtime/fleet revisions. Non-loopback URLs are
rejected and outbound requests are blocked. Browser temporary files and all raw
results stay under the same ignored study/run directory; no remote output writes.

`--validate-assets-live` opens one fresh paused test-mode context on **each** local
site and checks tick-zero readiness, AOT mode, bridge exports and captured assets.
It does not call Reset, Advance, PhysicsTiming, warmup or performance timers and
does not require a timing window. Captures are awaited after exact bridge readiness
and network idle, before measurement can begin. Asset requests use same-origin
`route.fetch(maxRedirects: 0)` and fulfil the browser request from that response;
hashes describe those loaded response-body bytes, not a separate refetch. This
avoids Chromium inspector-cache eviction for large native WASM. Redirects are
rejected. Late asset requests after the capture barrier fail explicitly. Existing
failed-run evidence is never modified. The existing project-local Playwright
browser cache is used automatically when no environment override is supplied.

For each churn profile (low=0.5, nominal=1, high=2), both revisions get at least
three fresh browser contexts and fresh battles with identical initial LightSwarm
configuration and seed. A separate warmup battle is disposed via Reset before the
measured battle starts at tick zero. Revision order alternates. The canvas's normal
simulation is paused (`?test=1` plus pause(true)); a synchronous page task uses
the inspected common `Advance(1)` export, so UI rendering cannot interleave with
the measured loop. `ProfileAdvance` is not exported in the inspected source and
its semantics are not assumed.

The old bridge's inner physics phases are explicitly unavailable/null. Its existing
DiagnosticStage physics/hits times remain *combined gameplay stages*, not a made-up
search/solver split. New `ResetPhysicsProfile(profile,seed,collect)` and
`PhysicsTiming(0..3)` provide the four agreed inner phases. The new side also gets
separate fresh `collect=false` outer controls and `collect=true` passes so timestamp
overhead is visible. Diagnostics are read outside the Advance/export outer timer.
Missing new profiling exports, non-AOT runtime, wrong fleet/initial config or JS
errors fail explicitly and retain local evidence.

This is a production before/after **runtime physics workload** comparison, not the
native externally prescribed scene experiment and not an isolated compiler speed
claim. Survival, hits, resulting entity counts and final checksums may intentionally
diverge; raw rows preserve those differences and never assert cross-engine checksum
parity. There is no FPS/render-throughput claim and historical evidence is untouched.
