English | [Русский](README.ru.md)

[Report](REPORT.md) | [Project README](../../README.md)

# Space Battle Benchmark

Interactive clients now use [corner warp reinforcements](REINFORCEMENTS.md)
for endless battles. This opt-in gameplay rule does not rewrite this historical campaign.

The [current gameplay cover workflow](COVER.md) is separate from this historical
benchmark campaign. Its authoritative configuration is `cover-settings.json`;
it updates only `cover.png` and `cover.png.json`, never frozen `action.png`,
`action.png.json`, parity records or measured results.

> **Revision boundary:** current gameplay is [mounted armament `mounted-armament-v7`](ARMAMENT.md), with [tactical AI `tactical-lock-v2`](TACTICS.md). The report and frozen evidence use `predictive-neutral-v1`. No new timing campaign was run. The full-suite/offline-analysis instructions below describe that historical campaign; the analyzer deliberately accepts only its v1 evidence. Do not overwrite frozen results with current runs or treat old measurements as current-gameplay performance. A new campaign needs separate outputs and an explicitly updated evidence gate.

V7 excludes the firing ship from projectile collision searches, including
all turret-parent fixtures. Other friendly/enemy hits, debris and hull contacts
are unchanged. `SelfHits` remains in telemetry for compatibility, always zero.
Historical evidence below retains its original collision rules.

V6 makes mounted guns auxiliary: cannon 7 damage / 84 ticks, plasma 3.25 / 36.
Damage is quartered and cooldown doubled: raw battery DPS is 87.5% lower.
Main/heavy weapons retain v5 settings; small-ship weapons are unchanged.

Heavy cadence v5 uses nominal cooldowns of 12/14/18/22/26 seconds for
Frigate/Destroyer/LightCarrier/FleetCarrier/Foundry; low doubles them and high
halves them. Per-shot damage remains 60/80/480/640/800; range, projectile speed,
heat and normal guns are unchanged. `SpawnShip` assigns 50%-100% initial charge
of the actual churn-scaled cooldown relative to `birthTick`, using a local RNG
from seed + ship serial independent of the battle RNG. Late-built/respawned
ships also have no free first heavy volley; turrets remain ready at birth.
This replaces the old nominal opening of 54 ready heavy weapons in 198 ships
and cooldowns of 3.5/4/6/7/8 seconds. Cadence changes sustained firepower, not
per-shot lethality; it is initial tuning, not balance proof. Tactical v2 remains
unchanged, with catalog rate feeding estimated DPS.

V6 passed exact catalog turret damage/cadence checks and all five 3000-tick
adapter replays (`0D9F42BC28101A28`), native render/controls and production AOT
combat/profile/reset/statistics/control/armament checks. This verifies
implementation, not matchup balance or performance.

V5 passed mechanics validation and all five 3000-tick adapter replays
(`F42899B97415194A`); AOT opening/combat/control checks also passed. These are
correctness checks, not timing measurements. Frozen results/reports and historical
spatial/performance studies are unchanged. Current checks use separate local outputs,
verify scaled repeat cooldowns and birth-relative charge for initial/late-built/
respawned ships, and check that charge sampling does not consume the battle RNG.

The headless host measures the shared `Battle<TStore>.Step` workload using five
native ECS implementations. Do not run analysis, rendering, replay, builds or
profilers concurrently with the main timing suite. All commands below are local;
none publishes, pushes or changes remote state. Run from the project root.

## Build And Validate

Build against the existing local package cache before starting timed runs.

```powershell
dotnet build SpaceBattle.Headless/SpaceBattle.Headless.csproj -c Release --artifacts-path Benchmarks.Local/tmp/tactics-build -m:1
dotnet Benchmarks.Local/tmp/tactics-build/bin/SpaceBattle.Headless/release/SpaceBattle.Headless.dll --validate
dotnet Benchmarks.Local/tmp/tactics-build/bin/SpaceBattle.Headless/release/SpaceBattle.Headless.dll --validate-adapters
dotnet Benchmarks.Local/tmp/tactics-build/bin/SpaceBattle.Headless/release/SpaceBattle.Headless.dll --validate-ports
```

## Full Suite

Run only after validation, sequentially with the same Release binaries and
environment. Warmup is a separate battle; measured gameplay starts at tick zero.
After the new aiming/projectile semantics below are implemented, rebuild and
rerun all 15 cases. Do not analyze the old suite, even if its five ports match.
Do not mix pre-change and post-change results.
These 15 runs measure 1,620,000 ticks, each covering 30 simulated minutes at 60 Hz.
They are unpaced by default, not 30 minutes of wall time. `--realtime` opts into
wall pacing but is deliberately excluded from this comparison configuration.

```powershell
& ./tools/space-battle/run-suite.ps1 -SkipBuild
```

`run-suite.ps1` is the authoritative suite command. It sets the local package
environment and `DOTNET_TieredCompilation=0`, runs sequentially and checks
checkpoints/final checksum/counters within each profile. `-SkipBuild` uses the
already-built Release binaries. Its execution order rotates by profile:

- low: secs, lite, classic, morpeh, static.
- nominal: classic, morpeh, static, secs, lite.
- high: static, secs, lite, classic, morpeh.

Rotation does not replace randomized repeated trials. Script `-Ticks` and
`-Warmup` overrides are useful for smoke checks, but the analyzer accepts only
the full 108000/600 configuration. The separate offline analyzer applies the
stricter complete-suite/schema/environment/provenance gate described below.

Host switches: `--framework secs|lite|classic|morpeh|static`, `--ticks`, `--seconds`
(used only when ticks is absent), `--warmup`, `--seed`, `--ships-per-team`,
`--churn`, `--particles`, `--output`, `--realtime`, plus validation switches above.
JSON and adjacent `.ticks.csv` are emitted after each run; both are required by
analysis. Never overwrite or mix
evidence from another build, environment or configuration in the same results set.

## Offline Analysis

Only after all campaign timings have finished, collect the read-only hardware
inventory. Do not invoke CIM queries during the timed campaign:

```powershell
& ./tools/space-battle/capture-hardware.ps1
```

`hardware.json` is required: Machine must match the headless suite, CapturedAt
must be after the run metadata dates, and CPU/RAM fields must be present. The
report includes CPU names, core/thread counts, RAM and frontend GPU/driver.
MaxClockSpeed is the nominal CIM maximum in MHz, not measured benchmark clocks.

After the entire timing suite has finished, use the locally bundled Python and
the existing `Benchmarks.Local/python-packages` NumPy/Matplotlib installation:

```powershell
$python = 'C:\Users\short\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
& $python -B tools/space-battle/analyze.py
if ($LASTEXITCODE -ne 0) { throw 'FAIL: analysis or fairness gate' }
```

No installation or network access is needed. The script uses standard JSON/CSV
plus local NumPy/Matplotlib with a noninteractive Agg backend and project-local
Matplotlib cache. Outputs are English `REPORT.md`, Russian `REPORT.ru.md`, `summary.csv`, `windows.csv`,
`charts/*.png` and `heatmap/*.svg` under this directory. The report is generated, not hand-maintained.
Do not invoke the analyzer while benchmarks are timing.

Comparative timing, allocation and memory tables use fixed lag thresholds relative
to the best result: up to 5% green, 5-15% green-to-yellow, 15-30% yellow-to-red,
from 30% red. Local SVG markers and unchanged text values work in GitHub Markdown.
The nine color steps are shared by all tables, never stretched to each group's
min/max. Entire groups within 5% are gray; numeric extremes are not bold.
Comparison groups and neutral diagnostics are defined in the report's
[color scale](REPORT.md#color-scale). The [heatmap helper](../../tools/space-battle/heatmap.py)
and [focused tests](../../tools/space-battle/test_heatmap.py) preserve this behavior
when reports are regenerated.

The 5/15/30% boundaries are agreed effect-size conventions, not significance
tests. Current evidence has one independent run per profile/framework: colors
show observed lag, not statistically significant differences. Ticks, contiguous
minute windows and tick `StddevMs` are not independent repeats. The helper accepts
explicit `IndependentRun(run_id, value)` summaries through `Metric.runs`, uses
their median rather than the luckiest run, and conservatively suppresses colors
when observed repeat envelopes do not separate beyond both tolerances. These
envelopes are not confidence intervals. Partial repeat coverage is gray; the
frozen analyzer inventory does not invent or import extra repeats.

Absolute timing tolerances are optional and scoped to an exact profile/metric.
The [default policy](../../tools/space-battle/heatmap_policy.json) disables all of
them (`null` by absence): no calibrated practical floor is supported by this
one-run dataset. Neither displayed precision nor the tick budget justifies a
universal floor, especially for stages with very different durations. This
default supports descriptive effect colors, not practical-significance claims.

To configure a justified floor, add an entry to `timing_absolute_thresholds`
with `profile`, `metric`, finite nonnegative `threshold_ms`, and nonempty
`reason_en`/`reason_ru`. State the target-specific acceptance criterion or
calibration behind the chosen milliseconds; the report prints each selection
and both rationales. Supported profiles are `low`, `nominal`, `high`; metric IDs
are `wall_seconds`, `tick.mean_ms`, `tick.p95_ms`, `tick.p99_ms`, `tick.max_ms`,
`phase.early.mean_ms`, `phase.middle.mean_ms`, `phase.late.mean_ms`, and
`stage.<stage-name>.mean_ms` for each of the nine stages. There is no wildcard or
automatic inheritance across stages, profiles or percentiles. All thresholds
are specified in ms; wall-duration comparisons convert them to seconds.
Use `analyze.py --heatmap-policy <project-local-policy.json>` for a separate
policy, or edit the default. This changes presentation only, not measurements.
All-zero groups are gray; a positive lower-is-better value against zero is red
only beyond the absolute floor, without division by zero or an invented epsilon.

Static syntax validation without execution, imports or generated files:

```powershell
& $python -B -c "import ast,pathlib; ast.parse(pathlib.Path('tools/space-battle/analyze.py').read_text(encoding='utf-8')); print('AST OK')"
```

The input schema is the PascalCase JSON serialized by
`SpaceBattle.Headless/Program.cs` with `IncludeFields=true`; gameplay counters are
fields and must not disappear. Only `{low,nominal,high}-{secs,lite,classic,morpeh,static}.json`
are accepted, excluding `smoke*.json`. Unexpected non-smoke JSON fails the gate.
Each run must match 108000 ticks, warmup 600, seed 7331, 96 escorts/team, default
remaining options, churn 0.5/1/2, and exactly 30 contiguous 3600-tick windows and
checkpoints. All checkpoints, final `Checksum`, full final `Counters` and every
window's logical counters/entities must match across all five within each profile.
`Metadata.TieredCompilation` must be the string `'0'` in every run.
All 15 runs must have `Metadata.GameplayRules.Revision='predictive-neutral-v1'`,
`ImpreciseAimProbability=0.25`, `MaxSpreadRadians=0.03`, `ProjectileTeam='none'`
and the current aim/collision-immunity/friendly-fire attribution descriptions.
Missing or old rules fail even when all five old outputs match. The full counter
schema includes `AimDecisions`, `ImpreciseAimDecisions`, `FriendlyFireHits` and
`SelfHits`, including each cumulative window. Imprecise decisions cannot exceed
all aim decisions; SelfHits must be a subset of FriendlyFireHits, itself a subset
of Hits. GameplayRules metadata must also agree across the full suite.
Runtime/machine/scope and source pins/binary hashes must be consistent. Mismatch,
missing data or invalid metrics writes a loud FAIL report and exits nonzero.
Recorded binary hashes must also match the current local Headless Release build;
consistent results from an older build are rejected. Hash consistency alone is
not a semantic-version check: the new rules still require the new build and full
rerun, not a report-text change over old results.
Old charts/CSVs are explicitly not evidence after failure. PASS is not proof of
statistical robustness or correctness beyond the recorded deterministic workload.

Graphs show all three profiles separately: minute mean/p95/p99 with a 16.67 ms
budget and logarithmic timing axis; managed heap, private process bytes and working
set in separate MiB charts; SECS native bytes alone, skipping null; live entities;
logical adds/removes per simulated second from cumulative-window differences;
and stacked stage means for every framework/profile. Global percentiles come from
full-run stats, never averages of window quantiles. Memory peaks are maxima of
minute-end samples, not continuously measured peaks. Allocation is managed
current-thread Step bytes/tick, not total memory usage; process memory includes
harness buffers. GC window counts are cumulative absolute values, not per-bin
collections. Structural counters exclude internal adapter markers. The first
structural bin includes initial composition because there is no tick-zero counter
snapshot; subsequent bins are true cumulative differences.

Headless `GcCollections` in JSON is a broader-interval delta: it is evaluated
after battle.Validate, DLL reads/SHA256 provenance and global Stats(timing).
It must not be labeled as gameplay-only or measured-loop-only GC. Likewise
`HarnessAndSimulationAllocatedBytes` includes between-Step harness telemetry and
post-loop validation/hashing/global timing aggregation, not just Step allocation.
These source fields are preserved and separately labeled in the report/summary.

The analyzer also derives GC deltas from raw tick CSV: final post-Step GC count
at tick 108000 minus first post-Step count at tick 1, separately for Gen0/1/2.
This sampled interval includes between-Step telemetry but excludes unknown GC
inside the first Step. No tick-zero GC baseline was persisted; it is not inferred.
The difference between broad JSON GC and CSV sample delta is unattributed: it
may arise before the first post-Step sample (including the first Step), after
the final sample (telemetry/post-loop work), or both. Even a difference of one
collection does not prove an extra post-loop GC. The absent tick-zero baseline
prevents separating these sources.
All 108000 tick rows, their order, monotonic GC samples, allocation totals, budget
counts and mean timing are checked. `summary.csv` uses explicit
`csvGcGen*DeltaPostStepTick1To108000` versus
`jsonGcGen*BroadIncludingPostLoop` columns, and a separately named broad harness
allocation total. Step `AllocatedBytes`/tick and tick quantiles retain their clean
Step scope. No raw input JSON/CSV is rewritten.

Charts use figure-level legends below all three panels, outside the plotted data.
The report's key outcomes are computed from the full suite: miss counts, observed
minimum means by profile, SECS nominal timing and the physics + hits share range.
These are one-run observations, not a universal winner or confidence estimate.

## Fair Native Adaptation

All five stores use the common `NativeStore` identity translation, reusable
generation slots and live reverse lookup, including SECS. Native ECS components
and native queries own data/membership; the bridge is not a shadow component
store. The shared identity-layer costs, including dictionary lookups, apply to
all five and may mask differences in their native APIs.
Classic/Lite/Morpeh private `LifetimeMarker` components retain empty entities
during composition, including until Commit.
These three adapters each carry one extra native component; logical layouts match,
but internal composition is not byte-identical. Morpeh/Static `Wrapper<T>` preserves
the unmanaged payload's actual `sizeof(T)`. No special SECS accessor/factory
benchmark APIs are exercised. Unknown `StoreDiagnostics` fields are null, not 0.
Common logical adds/removes exclude adapter markers. Aether service free-ID slots
are reused and bounded by the allocation high-water mark.

## Updated Battle Semantics

The `predictive-neutral-v1` methodology uses predictive AI aim solving the
constant-velocity intercept quadratic including muzzle offset and inherited
shooter velocity. A deterministic hash selects a small spread, up to 0.03 radians,
with probability 0.25 per firing decision/burst; the whole burst shares the same
offset. This does not guarantee exactly one quarter of short decision samples,
projectiles or hits. Both Projectile and Debris contain no Team; Owner is used
only for telemetry and Target for homing, neither grants immunity. Collidable
objects here are currently enabled ship fixtures. Particles/debris have no
health/body; projectile interception of other projectiles is not claimed.
Team membership does not grant collision-damage immunity. These changes affect trajectories, hit counts,
heat and subsequent battle feedback, so old timing/checksum evidence is stale.
Core implementation and its semantic validation are separate from this offline
analyzer. The report computes the observed `ImpreciseAimDecisions / AimDecisions`
ratio, not a ratio against Shots. `FriendlyFireHits` is observed only while the
owner Identity is still present in the ECS before Destroy/Commit, not necessarily
while its hull is positive. Full attribution is not retained after Identity removal.
`SelfHits` is a subset of FriendlyFireHits, not an additional disjoint count.
Counters/checkpoints establish matched traces across ports within each profile;
revision metadata and the observed ratio do not replace semantic Core validation.

Shared physics/hit resolution may dominate and mask ECS-specific performance.
Aether.Physics2D 2.2.0 uses compound fixtures and five-ray approximate projectile
CCD, not exact swept collision. Debris uses zero damping and is removed after
3600 lifetime ticks, on impact or outside 2.5 * ArenaHalfSize on either axis;
this is lifecycle cleanup, not drag. Validate rejects NaN/nonfinite states;
automatic NaN cleanup is not claimed. Timing excludes setup, snapshots,
validation and serialization. Headless budget headroom does not establish
frontend 60 FPS: rendering and UI cost are excluded.
Single hardware, one seed and no repeats do not support statistically robust
rankings. Simulated time is not realtime wall time. Network, rendering performance,
input latency and replay are not covered by this headless comparison.

## Relation To Microbenchmarks

`ShipsCreated` and `ShipsDestroyed` are logical ship Create/Destroy totals, not
total ECS entity lifecycle operations. Creation includes initial composition,
carrier replenishment and builds. `Shots` records projectile spawns, but the
schema has no cumulative particle/debris creation or total transient destruction
counters. Minute-end live counts cannot recover births/deaths between samples.
Do not add those live counts to ship totals or invent a complete transient-op
denominator. Structural adds/removes cover common logical component operations,
including real state churn such as overheating, combat, boosting, shield breaks
and docking; they are not counts of entity Create/Destroy calls.

The `lifecycle` stage mixes state changes, entity retirement, effects, builds,
structural commits and final collection/sorting. Dividing its time by ship,
shot or component counts would not establish Create/Destroy ns/op. The previous
flat-iteration microbenchmark win does not predict aggregate Step performance:
this workload also includes shared physics/five-ray CCD, AI, sorting and random
state/component accesses through native stores and the identity bridge. Stage
means and churn rates describe this workload; they do not isolate an ECS primitive
or attribute an aggregate difference to iteration alone.

## Frontend Evidence And Provenance

`action.png` is a real frontend framebuffer capture, not an analyzer rendering.
Its existing sidecar describes 1600 x 1000, 256 escorts/team, tick 900,
zoom 2. This is distinct from the normal 96 escorts/team benchmark. Snapshot ship
count, team alive counts (including docking state) and clipped rendered count
must not be conflated. The analyzer verifies sidecar configuration and nonblank
scene evidence and places this opening screenshot near the top after language navigation;
it never launches the frontend or manufactures a screenshot. The current sidecar
does not serialize seed. Seed 7331 is validated through `Metadata.Options.Seed`
in the paired `custom/frontend-capture-parity.json`, whose checksum and
Core/adapter hashes match the current action sidecar, and the known capture
command. It is not presented as a field read from the PNG sidecar.

[Frontend capture contract](../../SpaceBattle.Game/README.md).
The report requires `frontend.json` and `frontend.csv` from the separate
`FrontendProfiler` run. SchemaVersion 1 uses 3600 measured frames, 120 warmup,
ReplayStartTick 900 and measured ticks 1021-4620, exactly one Step/frame,
uncapped/VSync off. The current frozen case is SECS, seed 7331, 96 escorts/team,
1440 x 900, zoom 1, hidden window, atlas and verified controls. Do not profile
concurrently with headless timings. To reproduce this case in a separate period:

```powershell
dotnet SpaceBattle.Game/bin/Release/net8.0/SpaceBattle.Game.dll --hidden --ships-per-team 96 --seed 7331 --width 1440 --height 900 --zoom 1 --capture-tick 900 --validate-controls --profile-frames 3600 --profile-warmup 120 --profile-output docs/space-battle/frontend.json --profile-csv docs/space-battle/frontend.csv
```

The analyzer recomputes Count/Min/Mean/P50/P95/P99/Max from all CSV rows using the
frontend's nearest-rank `ceil(p*N)-1` rule, separately from the headless quantile
definition. It checks frame/tick continuity, null present columns, FPS identities,
budget flags, wall timestamp boundaries and GPU sample accounting. Game/Core/SECS
adapter hashes must match current local Game Release binaries; Core/adapter must
also match the headless suite. GPU support, unavailable reason, null/zero samples,
query-ring exhaustion, GL errors and validation flags are reported explicitly.
GL errors, invalid supported-GPU data or inconsistent flags fail analysis; an
unsupported GPU with a stated reason and null RenderMs remains unavailable, not 0.

SimulationCpuMs covers Step plus rolling HUD timing; SnapshotCaptureCpuMs covers
Battle.Capture only; RenderSubmissionCpuMs covers scene/HUD/GL submission;
TotalCpuFrameMs covers the callback including query polling, excluding auto-present;
WallFrameIntervalMs includes auto-present/event loop between callbacks. GPU
GL_TIME_ELAPSED is elapsed GPU timeline including possible CPU-generation idle
gaps, not pure busy utilization. CPU/GPU times overlap and cannot be added or
subtracted to infer present. PresentCpuMs/PresentWaitMs are null.
RealTimeGameplayFpsMeasured=false: replay throughput is not a frontend 60 FPS claim.
Fast-forward, warmup, final GL.Finish query drain and file/hash aggregation are excluded.

`custom/frontend-capture-parity.json` and `custom/frontend-profile-parity.json`
are required state-parity evidence, not extra performance cases. The analyzer
checks exact battle options, revision, Core/adapter hashes and final checkpoints
against capture tick 900 / 256 escorts (76139FC912B8B3E7) and frontend final tick
4620 / 96 escorts (D4DEA4EA08683883). Their warmup=0 timings/JIT tails are excluded
from benchmark statistics. The report hashes these artifacts and conditionally
references PLAN E7 as main-owner-reported validation, not tests rerun by analysis.
[Generated asset provenance and full prompt](../../SpaceBattle.Game/Assets/PROVENANCE.md).
Each run already records pinned vendor
revisions and binary SHA256 in `Provenance`; SECS is described as an optimized
working tree, not a clean commit. The report additionally hashes each input JSON.

## Logical Composition Diagnostics

[SYSTEMS.md](SYSTEMS.md) describes all nine timed stages, their queries and
component reads/writes, 22 attached component types, the unused declared
`Lifetime`, and stable entity compositions. The generated report embeds this
catalogue and the separately measured composition statistics. The Russian report
uses the equivalent [Russian catalogue](SYSTEMS.ru.md); both catalogues describe
the same stages, logical types and compositions.

The diagnostic tool references the existing Release DLLs rather than rebuilding
Core or adapters. Run it after the main suite, outside any performance measurement:

```powershell
$env:DOTNET_CLI_HOME="$PWD\Benchmarks.Local\.dotnet"
$env:NUGET_PACKAGES="$PWD\Benchmarks.Local\nuget-packages"
dotnet restore tools/space-battle/composition/Composition.csproj --source "$PWD\Benchmarks.Local\nuget-packages" -p:NuGetAudit=false
dotnet build tools/space-battle/composition/Composition.csproj -c Release --no-restore
dotnet tools/space-battle/composition/bin/Release/net8.0/Composition.dll --selftest
$env:DOTNET_TieredCompilation="0"
dotnet tools/space-battle/composition/bin/Release/net8.0/Composition.dll
```

The fixed suite is three SECS replays, seed 7331, churn 0.5/1/2 and 108000
end-of-Step samples each. The observer stores component-presence masks and
operation counters, not copied component values; all storage and queries still
execute in SECS. It verifies measured DLL hashes, every tick's entity/structural
trace, all 30 checkpoints and final gameplay counters. Native queries validate
the counts of all 23 declared logical types at tick zero and every 3600 ticks.
This is not an additional timing case or a census of every component in all five
frameworks. Diagnostic timing/allocation is excluded from the performance tables.

`composition/{profile}.json` contains initial/final/mean/min/max counts, entity-tick
shares, logical payload sizes, per-type additions/removals, Get/Has calls, query
ref bindings, full composition frequencies and count histograms. Its paired
`.ticks.csv` contains every sample's component counts, family counts/component
totals and histogram. `composition_report.py` checks these CSVs independently
against the original tick trace and recalculates reported aggregates.

The principal mean is sum(component instances over ticks) / sum(live entities
over ticks). This lifetime-weighted population mean is not a mean over unique
created entities or an arithmetic mean of each tick's ratio; the latter is
reported separately. Setup/warmup and validation/capture/checksum calls are
excluded. Objects created and destroyed within one Step contribute to operation
counts, but not to the end-of-tick population. Query bindings are delivered refs,
not measured field reads/writes, cache misses or memory-access cost. Internal
adapter calls and render calls are excluded. Private native LifetimeMarkers in
Classic/Lite/Morpeh are excluded from the shared logical composition.

After producing the three diagnostic artifacts, rerun `tools/space-battle/analyze.py`
with the configured Python runtime to validate and regenerate `REPORT.md` (English
by default), `REPORT.ru.md` and three composition charts from the same validated data.
`report_locale.py` supplies the English prose catalogue; `SYSTEMS.ru.md` is the
canonical Russian systems catalogue source, paired with the equivalent English
`SYSTEMS.md` for embedding. The observer DLL hash must match the stored diagnostics;
after editing/rebuilding the observer, regenerate its diagnostics too.
Focused validation rejection tests are in `tools/space-battle/test_composition_report.py`;
run them with the same Python runtime as the analyzer.

The local GitHub documentation/link checker validates language pairs, heading
anchors, path case and links to ignored files without changing remote state:

```powershell
python tools/check_docs.py
```
