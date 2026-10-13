English | [Русский](PLAN.ru.md)

# Archived SpaceBattle Delivery Plan

Acceptance snapshot: **2026-10-11**, `predictive-neutral-v1` campaign. The original requirements, evidence ledger and validation boundaries follow. PASS entries refer to that revision and environment; unchecked items record that date, not a current backlog.

[Historical report](REPORT_2026-10-11.md) | [Current documentation](README.md) | [Current report](REPORT.md)

## Evidence Ledger

| ID | Observed/reported evidence | Scope and limits |
| --- | --- | --- |
| E1 | Recorded: Core `--validate` PASS: all 11 classes, plasma shield heat, live ship production/destruction, fragments, real structural churn and repeat determinism; forced docking/repair/undocking, overheating/cooling, shield regen delay, shield cell/heat sink use, and NaN hull rejection. | Mechanics validation, not a full 30-minute benchmark or proof of every boundary case. |
| E2 | Recorded: `SECS.Tests` Release 16/16 PASS after the `WorldOptions` default-value fix and added regression test. | SECS runtime regression evidence, not a substitute for five-port battle validation. |
| E3 | Recorded: 70,000 lifecycle cycles PASS for each of the four adapters. | Adapter lifecycle coverage; not 70,000 battle ticks and not long-run timing evidence. |
| E4 | Recorded: 3,000 battle ticks, ten checkpoints at 300-tick intervals, with all five backends matching final strong checksum `E4C28CD57C7E82EE`. | Current Core before the SECS query-cache optimization, reported semantically unchanged. Retain binary provenance; do not call this 30,000 ticks or 108,000-tick parity. |
| E5 | Historical framebuffer metadata recorded earlier: tick 900, 256 escorts/team, 1600x1000, zoom 2; 61 rendered ships, 692 projectiles, 1,969 debris, 518 particles; atlas/nonblank true. | Historical Core SHA-256 `510CF8BA0AA3C31570B33A4E23A23546A50204A13981556680BF922ED6068745`, checksum `8846FF333324DF49`. `action.png` has since been refreshed (E9); its current file is not evidence for these historical values. Command-logic self-tests were not OS event injection. |
| E6 | Recorded: the old full suite completed: 15 cases = churn 0.5/1/2 x five ECS, seed 7331, 96 escorts/team, 600 warmup ticks, 108,000 measured ticks/case. | **SUPERSEDED for current gameplay** by predictive aiming, 25% deterministic spread and team-neutral projectile collisions. Historical files are not independently certified here and must not be relabeled as new-rule results. |
| E7 | Recorded: fresh new-rule build: 0 errors / 0 warnings. `--validate` PASS includes crossing/shooter-motion/stationary intercepts, linear fallbacks, deterministic 75/25 spread, actual enemy/friendly/self projectile damage, counters, burst inherited velocity and prior Core mechanics. `--validate-ports` PASS: all five match ten checkpoints over 3,000 ticks; final strong checksum `666A96DD2647E74D`. `SECS.Tests` 16/16 PASS in both Debug and Release; four adapters each PASS 70,000 lifecycle cycles. | Fresh reported correctness evidence, not rerun by documentation owner. New checkpoint checksum is revision/scenario-specific; old E4 is not an expected value. Short parity/build/test PASS is not 108,000-tick measurement or full report completion. |
| E8 | Recorded: [frontend.json](frontend.json) profiling complete: 3,600 measured frames, 120 warmup frames, 1440x900, 96 escorts/team, one simulation step/render, VSync off, hidden window. CPU frame p99 `4.1131 ms`, wall-frame p99 `4.2057 ms`, GPU timeline p99 `1.068448 ms`; 3,600 positive GPU samples, zero null samples/errors; zero reported budget misses. | Frontend-only timing under this configuration, not pure ECS ranking or 30-minute headless evidence. GPU timeline is labeled separately from CPU/wall time; zero misses is limited to this profile. Documentation owner has not independently reaggregated the file. |
| E9 | Recorded: refreshed [action.png](action.png) visually inspected: tick 900, 256 escorts/team; 101 snapshot ships, 94 rendered ships, 125 alive, 294 projectiles, 1,554 debris, 861 particles; checksum `76139FC912B8B3E7`. Core SHA-256 `C33F0C46BD5725E342233BF456E2EACF412FDDAD2EF91C43827E513D3E9D4120`; Game SHA-256 `1F00945060A72CEA5062DE90475E0A99E51745A06E9A7E7EC539E13011EC1296`. | Fresh new-rule actual framebuffer and main's visual inspection. Snapshot/rendered/alive counts are retained as distinct reported counters, not assumed identical. Do not attribute E5's old checksum/hash/object counts to this refreshed file. |
| E10 | Recorded: custom headless/frontend parity PASS: capture tick 900 with 256/team matches `76139FC912B8B3E7`; profile state at tick 4,620 with 96/team matches `D4DEA4EA08683883`. Files: [capture parity](custom/frontend-capture-parity.json), [profile parity](custom/frontend-profile-parity.json). | **Correctness-only custom runs without warmup**, not benchmark data. Their reported JIT-affected maxima of roughly 94-116 ms must be excluded from performance comparisons and realtime claims. Profile-state tick 4,620 is distinct from its 3,600 measured render frames; do not infer one tick per measured sample from that endpoint. |
| E11 | Recorded: replacement new-rule campaign 15/15 COMPLETE, terminal exit 0, session `17687`: five ECS x low/nominal/high, 108,000 measured ticks and 600 warmup ticks per case, 1,620,000 measured ticks total. All five match 30 canonical checkpoints and counters within each profile. | Full campaign execution and scoped suite parity PASS as reported. Supersedes the interim 12/15 status. Equality is within profiles, not across different churn workloads; not per-tick full-state, all-seed or cross-platform proof. No build/run/poll performed by this documentation owner. |
| E12 | Recorded: final central analyzer PASS again with corrected GC scopes: strict gates for 15 x 108,000 ticks, frontend CSV 3,600 samples/statistics, current hashes/custom parity/hardware. Current action/p99/native-memory/stage visuals inspected by main; legend outside, images nonblank/correct. [REPORT.md](REPORT_2026-10-11.md) has the opening screenshot near the top after language navigation; key outcomes, metrics/data/caveats complete. New projects in `SECS.sln`, `dotnet sln list` valid, `git diff --check` PASS. | **FINAL REPORT GATE COMPLETE in reported scope.** Broad raw JSON GC counts and sampled CSV post-Step deltas explicitly separate, without first-tick/post-loop-exclusive misattribution. Tick timings/Step allocations valid. Source/binaries unchanged; no more build needed. Documentation owner did not rerun checks or inspect images; optional future experiments remain unperformed. |

E1/E4/E5 remain historical mechanics/parity/visual evidence for their recorded
revision, not fresh validation of E7. E2/E3 retain runtime/adapter-test value but
do not prove new gameplay correctness. E9 is the fresh report-image candidate;
retain its executable hashes and E10 parity evidence. Do not reuse E5 as current proof.

## New Gameplay Gates

These rules extend R02/R05/R11 without changing the original 16-gate numbering.
N1-N3 have reported new-rule validation PASS (E7); their scope is recorded below.
Any additional boundary checks not named in E7 remain pending. Core is owned by
the main agent; this document update does not execute or independently certify tests.

- [x] **PASS (reported E7) N1 - Predictive AI aiming:** Crossing, shooter-motion
  and stationary intercepts, linear fallbacks and burst inherited velocity pass.
  Required behavior: aim using target motion and projectile
  travel, accounting for inherited launcher velocity where applicable. Freeze a
  deterministic solution/fallback for stationary, lateral, approaching,
  unreachable and coincident targets; require finite output and consistent
  arithmetic across stores. The non-spread branch has no random aim offset.
  Friendly fire does not change hostile target selection into friendly targeting.
- [x] **PASS (reported E7) N2 - Small spread on 25% of shots:** Deterministic 75/25
  validation passes. Required behavior: deterministically select the
  spread branch with probability 0.25 and apply a bounded small angular offset;
  the other 75% use predictive aim without spread. Freeze the angular bound,
  RNG/seed keys and decision unit (emitted projectile versus fire request/burst)
  before measurement. Do not assume exactly one in four consecutive shots unless
  that schedule is explicitly chosen. Test both branches, bounds, repeatability
  and query-order independence; validate the selection distribution without
  requiring exactly 25% in a finite battle sample.
- [x] **PASS (reported E7) N3 - Team-neutral projectile collision:** Actual enemy,
  friendly and self projectile damage plus counters pass. A launched projectile
  can hit any enabled ship, enemy or friendly, including its owner on a later
  collision. Team/owner metadata must not act as permanent collision exclusions.
  Any spawn-overlap protection must be explicitly scoped to launch, not the
  whole first sweep or lifetime. Test ally blocking before the intended enemy,
  friendly shield/hull/plasma damage and later owner re-entry, including guided
  projectiles. Require canonical nearest-hit/tie ordering and single-hit
  retirement regardless of fixture count or faction.
- [x] **PASS scoped new-rule revalidation/full-suite parity E7/E11 N4:** New mechanics and
  all-five short checkpoint parity pass; final checksum `666A96DD2647E74D`.
  Replacement campaign completes all 15 cases with 30 matching canonical
  checkpoints and counters for all five stores within each of three profiles.
  Preserve fresh checksums/build hashes; E4 is not an expected new checksum.
  Central raw-file/statistics/hash/hardware and final analyzer/presentation checks
  PASS as reported in E12; R16 reporting is complete in that scope. Keep historical provenance
  separate: never pool old/new ticks or compare them as equivalent workloads.
  Recheck sustained combat because aim/friendly fire change deaths, manufacture,
  docking and structural load. Final screenshot must come from the new-rule build.

Local implementation boundaries: [Core](../../SpaceBattle.Core/),
[headless runner](../../SpaceBattle.Headless/), [game](../../SpaceBattle.Game/),
[SECS store](../../SpaceBattle.SECS/), and [four adapters](../../SpaceBattle.Adapters/).

Scope correction: passive out-of-combat regeneration is required for **shields**.
Hull is restored by docking repair or repair kits; passive hull regeneration is
not an additional requirement. The earlier hull-regeneration review allegation
is withdrawn. Dock repair and shield regeneration are separate acceptance checks.

## Scope and Dependency Policy

- Implement a real autonomous 2D battle, not an ECS iteration-only fixture.
- Provide headless execution and a generated-sprite Silk.NET frontend over the
  same simulation; rendering must never decide simulation state.
- Compare SECS, LeoECS Lite, LeoECS Classic, Morpeh, and StaticEcs 1.2.01.
- Reuse the fixed local dependencies and runtime configuration. Do not upgrade,
  fetch replacements, substitute frameworks, or modify vendored sources to make
  a port faster. Record exact dependency provenance in the run manifest.
- Preserve dirty changes. Implementation work is local to this project; no
  publication, push, release, or remote mutation is part of acceptance.

## Contract and Determinism Gate

The implemented contract is `IEcsStore` plus `Battle<TStore>`, not the previously
proposed `IBattleBackend`. The battle uses shared rules over native ECS storage.
`IProcessor` and `ForEach` provide native queries for one, two, or three
components; store operations include create/destroy, ref `Get`, `Has`, `Set`,
`Remove`, `Commit`, diagnostics and disposal. Each backend owns its native world.

| Boundary | Required responsibility |
| --- | --- |
| `BattleConfig` / `ShipCatalog` / components | Configurable seed, tick rate, fleet/capacity/churn settings, class/loadout definitions and state; not a claim that every gameplay constant has an external schema. |
| `IEcsStore` / `Battle<TStore>` | Native component storage and 1/2/3-component queries; shared ordered battle stages, canonical inspection, counters, diagnostics and disposal. |
| Shared rules | Pure movement, targeting, weapon, damage, heat, inventory, and carrier calculations with explicit inputs/outputs. |
| Backend stage | Native ECS queries/filters, component references, structural operations, and entity-to-logical-ID mapping. |
| Command buffer | Stable-key structural/event commands committed at specified stage boundaries. |
| Render snapshot | Read-only ship/projectile/debris state keyed by logical IDs; extraction has a separate timer. |
| Run manifest | Config/hash, seed, backend/library provenance, executable hash, runtime/machine settings, and instrumentation mode. |

- [x] **IMPLEMENTED C1:** Actual component/store contract is shared across five
  native stores. Dispatch queries per stage/batch, not through a virtual adapter for every
  entity/component. Do not implement all ports as one dictionary/array engine
  behind five labels. Hot state and iteration must live in the selected ECS.
- [ ] **PENDING C2:** Fix `dt = 1 / 60`, units, rounding, contact tolerances,
  damage ordering, and resource caps. Use the same numeric operations in all
  ports. Same-platform runs require exact canonical state equality; tolerance
  comparisons are diagnostic only and cannot silently certify a port.
- [x] **IMPLEMENTED C3:** Stable serials are independent of backend handles;
  seeded `RandomStream` is used with deterministic event seeds and canonical
  processing order. Undocking parity uses serial, not `e.Id`; build ordering
  uses canonical owner serial. This is not a claim of a counter-based RNG.
- [ ] **PENDING C4:** Native query traversal may differ, but order-sensitive
  decisions must not: read stage-start state, break targeting ties by logical ID,
  sort collision pairs/commands by stable keys, and commit in defined order.
  Include deterministic broad-phase candidate ordering and solver iterations.
  Charge all necessary staging/sorting/command work to the measured tick.
- [x] **IMPLEMENTED C5:** Share equations and constants, not precomputed battle
  outcomes. Each port performs the same searches, contacts, inventory changes,
  manufacture, lifecycle transitions, and destruction work.

Actual nine stage names from `Battle.cs`:

1. `collect`: gather and canonically sort entity rows and team counts.
2. `ai`: target, retreat and docking decisions.
3. `thermal-inventory`: cooling, shield regeneration, thermal/tag requests and item use.
4. `steering`: nonlinear desired velocity, acceleration, damping and boost.
5. `weapons`: primary/secondary fire requests and projectile creation/commit.
6. `carriers`: production requests and docked repair/release decisions.
7. `physics`: ship-body simulation and projectile/debris/particle movement.
8. `hits`: contact/projectile/debris damage and effects.
9. `lifecycle`: state changes, destruction, build/reinforcement spawning and commit.

`Tick` advances before `collect`. Newly fired projectiles move in the same tick;
ships built during lifecycle first act on the next tick. The nine stages, rather
than the earlier seven-stage proposal, define stage-timing labels in the report.

## Incremental Implementation Checklist

Each step has an independently checkable exit gate. Implemented work and reported
checks are distinguished from optional/unreported experiments. Final delivery
status is the R01-R16 matrix and E12, not unchecked historical design experiments.

- [ ] **PENDING P0 - Freeze inputs/contracts:** Capture dependency identities;
  agree contracts C1-C5, scenario/config schema, class definitions, stage order,
  seeds, overflow policies, and headless command surface with the core owner.
  Exit: reviewed config plus deterministic minimal replay and contract tests.
- [x] **VERIFIED (reported E1) P1 - Vertical slice:** One escort, one mother ship, steering,
  simple-body contact, one physical weapon, shields/hull, death, and debris.
  Exit: headless scenario exercises movement/fire/contact/death without NaN or
  stale handles; reference-state tests reproduce it twice.
- [x] **IMPLEMENTED; mechanics PASS (E1) P2 - Complete mechanics:** All classes, four weapons,
  compound bodies, inventories, thermal states, regen, carrier production and
  docking/repair. R01-R13 evidence and remaining boundary checks are mapped below;
  this status is not blanket certification of every proposed test.
- [x] **IMPLEMENTED; scoped parity PASS (E7/E11) P3 - Complete native ports:** Independent ECS-native
  stage adapters for Lite, Classic, Morpeh, and StaticEcs against shared rules.
  Evidence: 70,000 lifecycle cycles per adapter and ten matching battle checkpoints
  over 3,000 ticks; all three long-run profiles now also match 30 canonical
  checkpoints and counters across all five stores. Per-tick full-state equality is
  an optional untested stronger experiment, not an inferred result.
- [x] **IMPLEMENTED; scoped capture/profile/parity verification E8-E10 P4 - Frontend:** Generated sprite assets,
  add Silk.NET rendering, camera/zoom, backend/scenario/seed selection, pause,
  single-step, restart, and compact live counters. Exit: actual moving battle,
  readable silhouettes/effects, frontend/headless matching state at equal ticks.
  Actual framebuffer is captured; optional OS input injection and broader visual checks
  are not covered by command-logic self-tests.
- [ ] **IMPLEMENTED; partial acceptance P5 - Validation harness:** Fixture checks,
  strong canonical checksum and headless recording are implemented/reported.
  Event-ledger comparison, final run manifests, raw metrics and aggregators still
  require artifact verification; they are not all certified by E1-E4.
  Exit: deliberate corruption fails validation and statistics are independently
  recomputed from raw samples; smoke runs are explicitly not benchmark results.
- [x] **COMPLETE execution/scoped parity (reported E11) P6 - Measurement:** All 15
  fixed backend/churn cases complete with 108,000 measured ticks and 600 warmup
  ticks each; terminal exit 0; 1.62 million measured ticks. Frontend E8 is separate.
  E12 now reports central strict analysis PASS for campaign data and frontend
  statistics/provenance/hardware. Final GC labeling/legend and report review
  also pass in E12; no additional mandatory measurement run is required. Additional
  seeds/repeats are optional, not missing mandatory campaign cases.
- [x] **COMPLETE final analyzer/presentation checks (reported E12) P7 - Report:** Report
  and charts exist; main visually inspected action/timing-p99/native-memory/stages.
  Final legend and GC-scope corrections, analyzer rerun and presentation review
  are confirmed by main. Delivered report content: real frontend battle
  opening screenshot near the top after language navigation, charts/tables, reproducible commands, provenance, and limits.
  Exit: all R01-R16 have linked evidence and reviewer checks match raw artifacts.

## Requirement Acceptance Matrix

The 16 gates below map the full requested scope. Concrete numeric tuning belongs
in the frozen config, not in undocumented port-specific constants.
The original feature set is implemented and has reported runtime/mechanics
verification (E7, including prior Core mechanics); frontend evidence is E8-E10.
This is scoped verification, not a claim that every possible experiment passed.
R15 has full three-profile scoped parity evidence E11; R16's campaign execution
and central analysis plus final analyzer/presentation review are complete in
reported E12 scope. Optional experiments below do not become new
mandatory user requirements or make implemented features appear unimplemented.

| Gate / status | Implementation requirement | Explicit acceptance evidence |
| --- | --- | --- |
| R01 **IMPLEMENTED; runtime verified (reported)** | 2D acceleration, powered-ship damping, inertial projectiles/debris. | E7 prior mechanics PASS, shared motion systems and E9 actual battle. Isolated trajectory/damping/coast equation experiments remain unreported optional coverage; an image alone does not prove equations. |
| R02 **IMPLEMENTED; new aim PASS (reported E7)** | Nonlinear steering and predictive AI aiming. | E7 crossing/shooter-motion/stationary intercepts, linear fallbacks and burst inherited velocity pass. Extra trajectory/boundary checks not listed in E7 are not claimed. Obstacle avoidance is not an extra user requirement. |
| R03 **PASS (reported E1)** | Eight escort classes. | All 11 total ship classes validated; eight escort class definitions participate in Core. Preserve class-specific evidence in the final validation artifact. |
| R04 **PASS (reported E1)** | Three mother-ship classes. | All 11 classes include three mother classes; live manufacture and forced carrier docking/repair are covered. |
| R05 **IMPLEMENTED; new rules PASS (reported E7)** | Machine gun, cannon, plasma, missile; predictive aim, deterministic small spread 25% of the time and team-neutral hits. | E7 aim/fallback, deterministic 75/25, actual enemy/friendly/self damage, counters and burst inherited velocity PASS; prior Core mechanics also pass. Preserve exact per-weapon evidence in final validation artifacts; no unreported guidance/fixture boundary test is inferred. |
| R06 **IMPLEMENTED; runtime verified (reported)** | Simple shapes and compound 4-6-fixture bodies. | Shared collider definitions and E7 prior all-class mechanics PASS. Dedicated fixture-offset/contact-count experiments remain unreported; no separate exhaustive 4/5/6-fixture test PASS is claimed. |
| R07 **PASS (reported E1)** | Hull/shields; shield regeneration out of combat; hull repair by docking or repair kit. | Forced shield regen-delay and dock repair PASS; live destruction and NaN hull rejection PASS. Passive hull regeneration is not required. |
| R08 **IMPLEMENTED; mechanics verified (reported)** | Inventories of different sizes. | Class-dependent capacities, actual item use and E7 prior mechanics PASS; checksum includes capacity/use timer/all slots. Dedicated full/empty/insertion-capacity experiments remain unreported optional coverage, not a required insertion UI. |
| R09 **PASS (reported E1)** | Shield cells and heat sinks. | Forced shield-cell/heat-sink consumption PASS. Retain threshold/effect/stack evidence in validation output. |
| R10 **IMPLEMENTED; mechanics verified, residual coverage unreported** | Heat, overheat, cooling and plasma shield heat. | E7 prior forced overheat/cooling/plasma mechanics PASS. Exact tick-of-detection firing inhibition is a separately unreported boundary regression; do not claim that specific test passed. |
| R11 **IMPLEMENTED; new aim PASS (reported E7)** | All ships controlled by AI, including predictive aiming. | E7 predictive aim/spread and prior Core mechanics PASS. Separate target/home-loss boundary evidence remains pending if not in the reported validation; friendly fire does not require friendly target selection. |
| R12 **IMPLEMENTED; mechanics verified, residual coverage unreported** | Carriers manufacture, dock, repair and undock ships. | E7 prior live production/forced dock-repair-undock PASS; canonical build ordering and serial-based undock parity fixed. Simultaneous fleet-limit and carrier-death/docked-ship regressions are separately unreported. Manufacturing-resource economy is not required scope. |
| R13 **IMPLEMENTED; runtime verified (reported)** | Physical inertial shrapnel. | E7 prior fragment/churn mechanics PASS and E9 actual 1,554 debris; fragments are distinct from visual particles. Dedicated analytical trajectory/lifetime experiments remain unreported optional coverage, not an unsupported additional PASS. |
| R14 **IMPLEMENTED; fresh capture/profile/parity PASS (reported)** | Headless runner and generated-sprite Silk.NET frontend. | E8 profile, E9 visually inspected actual framebuffer and E10 capture/profile headless parity. Control command-logic tests still do not establish OS event injection; resized-window acceptance is not inferred from hidden-window profiling. |
| R15 **IMPLEMENTED/VERIFIED; suite parity and central checks PASS** | Equivalent SECS/Lite/Classic/Morpeh/StaticEcs 1.2.01 ports. | E7 runtime/adapter/short parity plus E11 full three-profile 30-checkpoint/counter parity; E12 central strict checks of current hashes/data/hardware PASS. Final report review is separate. No cross-platform/all-seed/per-tick full-state claim. |
| R16 **DELIVERED; final strict/visual verification PASS (reported E12)** | 60 ticks/s, 30 minutes of simulation, overtime report with real battle screenshot at top. | E11 complete campaign; E12 final analyzer/presentation PASS; opening screenshot after language navigation, key outcomes/metrics/data/caveats complete. Broad JSON GC counts and sampled CSV deltas separate. Timing/Step allocations valid; custom E10 timing excluded. No universal realtime/all-seed/cross-platform conclusion is implied. |

## Final Status Layout

Original R01-R16 are delivered in the scope documented above: R01-R13 gameplay
and mechanics checks; R14 actual frontend/headless/profile/parity; R15 five native
equivalent ports; R16 complete campaign and final report with the opening screenshot after language navigation.
New-rule aiming/spread/enemy-friendly-self collision checks are included. This
summary does not certify optional future experiments or unnamed regressions.

| Required deliverable | Current status | Evidence needed before final closeout |
| --- | --- | --- |
| Original R01-R13 mechanics plus new aim/spread/friendly fire | Implemented; reported scoped mechanics verification E7 | Keep exact validation scope/provenance, and disclose residual boundary gaps rather than inventing individual test results. |
| R14 headless and real Silk.NET frontend | Implemented; profile/capture/custom parity reported E8-E10 | Link current image/profile/parity artifacts in the report; do not use un-warmed custom durations as performance evidence. |
| R15 equivalent five ports | COMPLETE in reported scope: suite parity E11 and central checks E12 PASS | Retain checkpoint/counter/hash scope; do not imply optional every-tick/all-seed/cross-platform proof. |
| R16 30-minute 60 Hz campaign | Execution and central strict analysis COMPLETE in reported scope | Preserve honest budget-miss/statistics reporting; completed simulation alone is not proof of realtime budget compliance. GC totals retain their actual measurement scope. |
| R16 final report with opening screenshot after language navigation | COMPLETE; final analyzer/visual checks PASS (E12) | Actual screenshot near the top after language navigation, complete outcomes/metrics/data/caveats; corrected legend and explicit JSON/CSV GC scopes. Optional experiments remain outside closeout. |

### Optional Experiments and Residual Gaps

These remain **UNTESTED/UNREPORTED** unless separately evidenced. Their absence
does not add requirements to the user's original R01-R16 or invalidate the
specific reported PASS results. Neither does a reported PASS silently cover them.

- Optional portability/stochastic coverage: other platforms/architectures,
  all seeds, multi-seed repeat campaigns, repeat-variance/significance analysis.
- Optional stronger equivalence coverage: full canonical state equality every
  tick, complete physics-solver/cache serialization and full ordered event-ledger
  comparison. Current proof is checkpoints plus reported counter comparisons,
  not these stronger alternatives.
- Optional frontend/control coverage: OS event injection, VSync-on trials,
  additional window sizes/devices/drivers. Current control evidence is command
  logic; actual framebuffer and hidden VSync-off profile are separately evidenced.
- Optional isolated numerical/boundary experiments: analytical coast/damping,
  exhaustive fixture contacts/offsets, full/empty inventory insertion, additional
  intercept fallback geometries and target/home-loss scenarios beyond named tests.
- Residual correctness gaps, not erased or promoted to PASS: the exact tick of
  overheat firing inhibition, simultaneous-build fleet-limit reservation and
  carrier-death/docked-ship transitions lack separately named regression evidence
  in the supplied updates. This is a coverage limitation, not a fresh claim that
  current Core is broken; no current-code re-review was performed for this update.

## Workload and Correctness Protocol

- [ ] **PENDING V1:** Freeze a baseline scenario containing all classes/weapons,
  both shape categories, differing inventories and active carriers. Publish
  exact fleets, map, AI settings, manufacturing timing, fixture definitions,
  spawn schedule, entity/debris caps and lifespan. Additional larger scenarios
  are separate workload labels, not backend-specific adjustments.
- [ ] **PENDING V2:** Keep all 30 simulated minutes meaningful. Prefer a frozen,
  identical replenishment policy sufficient to sustain combat. Log
  active ships, shots, contacts, deaths, dock cycles and builds per minute.
  If battle ends early, flag idle tail and rerun an explicitly revised scenario
  for every backend; never hide it by truncating only some runs.
- [x] **IMPLEMENTED V3 - strong battle checksum:** Canonical state sorted by logical ID:
  components, float bit patterns, inventories, fixture state, targets, timers,
  queues, dock occupants, next serial, cooldowns/tags/item fields and future
  reinforcements. This describes Core state, not nonexistent resource/PRNG-counter
  components or a complete serialized physics-engine solver cache.
  Exclude backend handles, padding bytes, timings and render interpolation.
  Event-ledger comparison and complete physics-internal-state coverage are
  additional pending checks, not claims established by a battle-component hash.
- [ ] **REPORTED CHECKPOINT/COUNTER PASS E7/E11; optional stronger checks UNTESTED V4:** Run correctness separately from performance: optional full per-tick
  state equality on small fixtures and full 108,000-tick replay checksums/events
  for benchmark scenarios. Checkpoints and counters are the current campaign
  gates; do not require optional full-state/event-ledger experiments to call
  those gates passed. On first mismatch, save tick, stages and component
  diff and stop certifying that port. Compare a repeat to itself as well as to
  the reference; choosing SECS as reference does not prove its rules correct.
- [ ] **PARTIAL PASS (E1-E3); remaining gate PENDING V5:** NaN hull rejection,
  lifecycle checks and forced mechanics pass. Retain checks for finite values, inventory bounds, valid
  target/dock handles, no double death/hit/use, no leaked worlds, and eventual
  debris cleanup. Include generation reuse, structural churn and zero-entity tests.

## Benchmark Methodology

### Controlled Runs

- [ ] **PENDING B1:** Use identical Release builds, architecture, runtime, GC,
  JIT/tiering/PGO settings, CPU policy, thread count and capacity policy. Default
  to a single simulation thread for all five. Record OS, CPU/RAM, .NET version,
  build/source hashes including dirty patch identity, dependency hashes and date.
  Record process affinity if set; do not change machine-wide settings implicitly.
- [ ] **PENDING B2 - verify replacement-run policy:** Start each measured backend in a fresh process. Warm a
  disposable world for **600 ticks**, matching E6, with the same mechanics/config; exclude its
  timing, dispose, then initialize a fresh measured world. Use this same policy
  everywhere; record initialization/warmup/disposal separately. Do not force GC
  during measured ticks. Any pre-run collection policy must be identical and
  explicit; do not keep only one backend's warmed gameplay state.
- [x] **COMPLETE campaign E11; strict final output checks PASS E12 B3:** Replacement campaign is **15 cases**:
  `seed=7331`, `shipsPerTeam=96`, churn `0.5`, `1`, `2`, all five ECS, one case per
  combination. These are three workloads, not three repeats. Backend execution
  order/process policy must be retained in manifests. Keep every valid case and
  record failures; do not select best runs or discard slow outliers. Multi-seed
  and repeated-process uncertainty estimates remain optional follow-up work;
  current single-case values cannot support repeat-variance or significance claims.
- [ ] **PENDING B4:** Run 108,000 ticks with fixed `dt` and no sleeping, rendering,
  VSync, frame dropping or adaptive dt in the primary headless benchmark. Thus
  30 minutes is simulation time, not promised wall time. Evaluate realtime
  feasibility against `1000 / 60 = 16.6666667 ms` per complete tick. An optional
  paced run is separately labeled and includes scheduling lateness/backlog.

### Timing and Statistics

- [ ] **PENDING B5:** Use monotonic high-resolution timestamps. Tick time starts
  before the first simulation stage and ends after lifecycle commit; includes
  ECS queries, AI, physics, structural work and GC pauses caused within the tick.
  Exclude setup, warmup, serialization, checksum generation, rendering and idle
  waits. Store timestamp frequency and convert using floating-point arithmetic.
- [ ] **PENDING B6:** Capture every tick duration in preallocated buffers and
  write after measurement. Recorded: Headless records arrays and writes CSV
  only after each run; no per-tick CSV formatting/writes should contaminate
  measured allocation/GC. This implementation statement is not final file proof.
  No per-tick formatted logging or growing lists.
  Timer calls have overhead: measure empty-harness cost and compare primary
  total-tick runs with stage-instrumented diagnostic runs. Do not subtract an
  unverified constant or mix instrumentation modes in the ranking.
- [ ] **PENDING B7:** Report average, median, p95, p99, maximum, total wall time,
  ticks/s, miss count/rate (`tickMs > 1000/60`), total excess milliseconds, longest
  consecutive miss streak, and backlog for paced runs. Quantiles use documented
  nearest-rank `ceil(p*N)` indexing on sorted raw samples. Preserve max outliers.
- [ ] **PENDING B8:** Produce full-run and minute-bin statistics. Bin `m` contains
  ticks `[m*3600, (m+1)*3600)` for `m = 0..29`. Show active world counts beside
  timing to expose changes in workload. Produce corresponding per-stage bins
  for actual stages `collect`, `ai`, `thermal-inventory`, `steering`, `weapons`,
  `carriers`, `physics`, `hits`, `lifecycle`.
  Explain residual timer overhead; stage sums need not exactly equal tick time.
- [ ] **PENDING B9:** For the replacement E7 campaign, report each backend/churn case
  separately; no repeated-run variance exists merely because churn has three
  values. For optional repeat/multi-seed campaigns, aggregate first within each repeat, then show each seed's
  repeat distribution/range and paired backend comparisons under equal seeds.
  Do not average p99 values and call that pooled p99; calculate pooled quantiles
  from raw durations and label them separately. Any speed ratio must name its
  numerator/denominator and workload, not imply universal ECS superiority.

### Allocation, GC, and Memory

Current GC scope correction (E12): producer JSON `GcCollections` is read after
post-loop validation, provenance hashing and aggregation, so its counts cover a
broader interval than the clean tick-timing/Step-allocation measurements. Report
these as broader producer-interval counts, not exact simulation-only collections.
CSV counters sampled after each Step permit `last - first` deltas for the
post-first-Step to post-last-Step interval; this excludes any first-Step change
and is **not** an exact tick-0 baseline or complete 108,000-tick GC total. Do not
silently substitute those deltas for the broader JSON totals. Neither counter
series measures GC pause time. The legend/report must identify both scopes.

- [ ] **PENDING B10:** Record managed allocated-byte deltas over the measured
  interval via a process-wide allocation counter; retain per-minute deltas.
  Thread-only counters are acceptable only with proven single-thread allocation
  coverage. Report bytes/tick and total bytes, plus Gen0/1/2 collection deltas.
  GC pause durations require runtime-event diagnostics in a separately labeled
  run; collection count alone is not pause time.
- [ ] **PENDING B11:** Sample managed heap size, process private bytes and working
  set at initialization and minute boundaries, with sampled peaks. State that
  minute sampling cannot establish an exact transient peak. Record sampling
  overhead separately. No forced collections inside timed runs.
- [ ] **PENDING B12:** Report SECS native component/storage bytes and managed
  accessor payload as separate counters where supported; distinguish live use
  from retained capacity. These do not include allocator overhead or all process
  memory. Native/unavailable counters for other ECS must be marked unavailable,
  never zero. Avoid subtracting managed heap from private bytes and labeling
  the result as ECS-native memory.

### Separate Frontend Measurements

- [ ] **PROFILE RECORDED E8; manifest verification PENDING F1:** Freeze camera, resolution, sprite assets, seed, backend,
  visible entity counts, renderer and driver. Run identical replay/tick windows;
  simulation remains fixed-step with interpolation only in presentation.
- [ ] **PROFILE TIMING E8; complete breakdown verification PENDING F2:** CPU-frame,
  wall-frame and GPU-timeline p99 are recorded separately in E8. Retain simulation CPU, snapshot extraction CPU, render
  submission CPU, complete frame CPU, GPU render time, and present/VSync wait
  separately. Use GPU timestamp queries with delayed nonblocking readback;
  if unavailable, state that GPU timing is unavailable, not CPU render time.
- [x] **VSYNC-OFF PROFILE/REPORT VERIFIED E8/E12 F3:** Publish VSync-off throughput and optionally VSync-on user
  experience as separate cases. Track frame percentiles, dropped presentation
  frames and simulation catch-up/backlog; never conceal slow simulation by
  dropping simulation ticks. Do not use frontend totals to rank pure ECS cost.
- [x] **FRESH CAPTURE/PARITY E9-E10; report-first placement VERIFIED E12 F4:** Recorded:
  the new-rule framebuffer visually inspected at tick 900 and headless parity
  verified. Retain E9 executable hashes and E10 custom correctness artifacts.
  The report's opening screenshot must appear near the top after language
  navigation, with capture provenance immediately below.
  Generated sprites are assets, not substitute benchmark screenshots.

## Artifact and Final Review Gate

Full result files currently belong under `docs/space-battle/results/`; their
final names/schemas and completeness must be verified after the E7 rerun finishes. The
layout below remains a proposed archival structure, not a claim of existing files
or an instruction to rename live outputs. E5 already exists as `action.png` and
`action.png.json` beside this plan.

```text
docs/space-battle/runs/<run-id>/manifest.json
docs/space-battle/runs/<run-id>/validation.json
docs/space-battle/runs/<run-id>/ticks.csv
docs/space-battle/runs/<run-id>/stages.csv
docs/space-battle/runs/<run-id>/minutes.csv
docs/space-battle/runs/<run-id>/memory.csv
docs/space-battle/runs/<run-id>/frontend.csv
docs/space-battle/runs/<run-id>/battle.png
docs/space-battle/runs/<run-id>/REPORT.md
```

- [x] **DELIVERED with reported strict data/provenance verification E12 A1:** Raw records contain backend, seed, repeat, scenario, mode,
  tick/bin, units and schema version; manifest contains exact runnable commands.
  Command syntax is documented only after implementation, not invented as working.
- [x] **DELIVERED with reported final report/visual verification E12 A2:** Report includes the opening screenshot near the top after language navigation, scope/provenance, all-five
  validation matrix, full-run and 30-minute charts, stage costs, allocations/GC,
  managed/process/native memory distinctions, frontend CPU/GPU, and failures.
- [x] **PASS final central strict analyzer rerun (reported E12) A3:**
  Recorded: all-run/frontend statistics and current hashes/custom parity/
  hardware checks PASS. Required invariant: recalculation matches saved statistics, every
  duration sample count is 108,000, minute bins contain exactly 3,600 ticks, and
  screenshot metadata identifies an actual run state.
- [x] **DELIVERED with explicit evidence scopes E12 A4:** Build/test commands, smoke evidence, replay parity and full
  campaign are distinct. Compilation or a short run cannot satisfy R16. A
  successful comparison may show budget misses; report them as failures of the
  60 Hz budget on that machine, not as equivalent to sustained realtime success.
- [x] **DELIVERED original R01-R16 in reported scope E12 A5:** Requirements map to
  tests/artifacts; optional unperformed experiments remain named and unchecked.
  No unmeasured performance, unsupported portability, unsupported completed-work,
  or universal-speed claims appear in the report.
