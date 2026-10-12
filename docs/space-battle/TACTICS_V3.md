English | [Русский](TACTICS_V3.ru.md)

# Layered Squad Tactics

Current AI revision: **layered-squads-v3**. Desktop and Mono AOT share C# code.
[DamageRules](../../SpaceBattle.Core/DamageRules.cs) remains the only profile
source: damage revision layered-weapon-damage-v1, weapons, physics and artwork
are unchanged. Different decisions intentionally change trajectories/checksums.

## Composition And Persistence

Groups still contain 2-4 ships, Interceptor through Gunship. Team-specific
spatial snapshots feed greedy joint-cost recruitment, not combination enumeration
or a diversity quota. Mixed and homogeneous groups compete on usefulness.
Compatibility includes speed/range ratios, arrival delay, health and weapon/heat
readiness. Temporary screening/cooling preserves membership; death, docking,
class/team changes and large separation repair the roster and elect a survivor.
Periodic review replaces at most one member for a clear improvement. A shield
transition does not reset leases or pause useful fire while waiting for plasma.

[TacticsSettings](../../SpaceBattle.Core/TacticsSettings.cs) defaults are heuristics,
not proven balance: 120-tick reviews, 20% replacement improvement, minimum
speed/range ratios 0.45/0.35, 2.5-second arrival tolerance, 8-second engagement
ceiling. Target leases retain 120 ticks and the previous 35% switch threshold.

## Scoring And Movement

A bounded first-impact/continuous-rate model handles shield then hull through
real profiles, weapon readiness/cooldown, flight, hit probability, packet overkill
and own heat limits. Late participants do not contribute full DPS before arrival.
Return damage/expected losses and probabilistic plasma suppression are separate
from direct damage; suppression is capped at 25% cost benefit, not guaranteed
overheat or a reduction of HP. Flying allied shots receive at most 35% initial
confidence, attenuated by motion/heading; credited damage is capped at half each
remaining layer. Actual CCD and friendly fire are untouched.

Target-relative angular lanes use individual working ranges, short separation
and steering away from an ally crossing a firing ray. This reduces intended
overlap, but neither guarantees a clear path nor immunizes allies. Wounded ships
still screen behind healthier ships, recover and return through existing states.

## Budgets And Limits

Defaults: 16384 spatial cell/membership visits and 8192 target-cost evaluations
per phase, 2048 composition evaluations per review; 16 local targets, 8 rotating
global probes, 6 formation targets, 16 recruits and 4096 projectile intents.
Sealed indexes and reusable buffers avoid reserved hot-path allocations.
Recruitment filters assigned/ineligible rows before filling the bounded sample,
so an assigned prefix cannot permanently starve a dense cell. When spatial work
is exhausted, a constant-sized neighboring serial window allows stable progress.

Limited spatial queries are deterministic samples, not globally optimal nearest
or target searches. Stable traversal is followed by snapshot/serial sorting.
Current-velocity engagement bounds plus rotating global probes may still omit a
good target until a later review. On exhaustion, live leases and existing groups
are retained; sampled compatible free recruits provide a stable fallback.
Diagnostics count fallback explicitly.

These counters bound spatial visits and expensive score calls, not all AI CPU
time: compatibility/arrival prefilters and steering also do work. Snapshot/index
preparation is linear; two additional team indexes require roughly 0.5 MiB of
cell metadata plus population-sized buffers. Fully coupled dense physical
contacts still have a quadratic worst case; physics was not changed here.

Approximation limits: future acceleration, sustained discrete volleys, regen/
repair, changing heat and exact turret aim/obstruction. Large solo ships retain
catalog mount pairs, not arbitrary external gun edits. Cross-platform bitwise
determinism and a win in every scenario are not claimed.

## Validation

[Squad tests](../../SpaceBattle.Headless/SquadCompositionValidation.cs) cover
composition, compatibility, leases, death repair, cover, lanes and dense budgets.
[Scoring tests](../../SpaceBattle.Headless/TacticalScoringValidation.cs) cover
layer/arrival/heat estimates. Full checks and all-port replays remain required.

[TacticsStudy](../../tools/TacticsStudy/Program.cs) compares frozen v2 against v3
with identical damage, fleet, seed, particles and physics. Independent processes
alternate variants with separate warmup worlds. Step/AI exclude observation,
export and verification. Both team losses are cumulative; TTK is censored
exposure-to-death, not an isolated duel. Whole-Step timings are outcome/population
dependent, not pure kernel speed. New raw data stays in ignored Benchmarks.Local/tmp.

## Local Comparison

2026-10-12, Windows x64, .NET 8.0.31 Release SECS, tiering/ReadyToRun off.
Nominal finite LightSwarm: 96 escorts and 3 carriers per team, 60 Hz, unchanged
damage/physics/particles. Seeds 27, 7331, 61012; three independent processes per
seed/variant (18 total), alternating order, no concurrent builds/measurements.
Each process uses a separate 2400-tick warmup, a fresh 2400-tick measured battle,
then an untimed replay. Source/binary/config hashes and identical replay outcomes
within each variant were verified. Different AI revisions intentionally differ.

Milliseconds below are median independent process means with their min-max range.
Tail values are medians of each process's tick p95/p99, not independent trials.

| Seed | Revision | Step mean [range], ms | Step p95 / p99 | AI mean [range], ms | AI p95 / p99 |
| --- | --- | --- | --- | --- | --- |
| 27 | v2 | 0.2875 [0.2842-0.2893] | 0.8541 / 1.1821 | 0.0206 [0.0205-0.0206] | 0.0621 / 0.1673 |
| 27 | v3 | 0.3122 [0.3043-0.3160] | 0.9818 / 1.5710 | 0.0490 [0.0480-0.0505] | 0.1704 / 0.4468 |
| 7331 | v2 | 0.2708 [0.2695-0.2755] | 0.8304 / 1.1077 | 0.0192 [0.0188-0.0193] | 0.0593 / 0.1689 |
| 7331 | v3 | 0.3219 [0.3149-0.3271] | 0.9944 / 1.4220 | 0.0524 [0.0518-0.0534] | 0.1906 / 0.4543 |
| 61012 | v2 | 0.2724 [0.2700-0.2800] | 0.8628 / 1.0487 | 0.0192 [0.0190-0.0194] | 0.0604 / 0.1437 |
| 61012 | v3 | 0.3318 [0.3279-0.3329] | 0.9734 / 1.3475 | 0.0527 [0.0523-0.0533] | 0.1753 / 0.4229 |

Gameplay is counted once per seed/variant, not multiplied by timing repetitions.
Losses include newborns; group transitions are observer sums over actual leader
handle changes, not the new internal GroupChanges diagnostic.

| Seed | Revision | Cyan / red deaths | Friendly-fire hits | Target switches | Group membership changes | Exposure-to-death KM median, s | Exposed / right-censored |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 27 | v2 | 120 / 109 | 4076 | 3154 | 575 | 2.933 | 198 / 13 |
| 27 | v3 | 120 / 117 | 3206 | 1633 | 569 | 2.867 | 198 / 7 |
| 7331 | v2 | 115 / 119 | 3880 | 3221 | 523 | 2.650 | 198 / 8 |
| 7331 | v3 | 118 / 113 | 3113 | 1871 | 549 | 2.817 | 197 / 11 |
| 61012 | v2 | 118 / 118 | 4263 | 3066 | 646 | 2.983 | 198 / 7 |
| 61012 | v3 | 120 / 115 | 3206 | 1848 | 546 | 3.217 | 197 / 7 |

Kaplan-Meier uses initially present ships from first observed incoming target or
damage to death/end-of-window. Same-time deaths precede censor removal. One
unexposed initial death in each of the latter two v3 seeds is explicitly excluded.
Pooling each seed once gives 2.867 -> 2.983 s (594 -> 592 exposed ships). This is
descriptive exposure-to-death in an interacting battle, not controlled duel TTK:
exposure/cohorts differ, censoring can be informative, and lethal first hits or
within-tick changes can be missed. No significance or independent-ship claim.

Observed improvements: friendly-fire hits -19.8% to -24.8%; target switches
-39.7% to -48.2%. Regressions are retained: Step +8.6%/+18.9%/+21.8%, AI cost
2.38x/2.73x/2.75x, higher tails, more group transitions on seed 7331, longer
exposure medians on two seeds, and mixed team losses (seed 27 loses eight more
red ships). This is richer tactics, not a performance or universal balance win.
Budgets cap costly work but do not make it free.

V3 work totals across 2400 ticks for those seeds: 96359/104550/106157 score
evaluations, 395783/423659/427469 spatial visits, 247/254/249 formation reviews,
97/94/97 fallback notifications. Reserved PrepareTactics allocation tests report
zero bytes on all five adapters; whole Step still allocates 3.13-3.24 MB over a
battle for existing structural work/capacity growth (v2: 3.28-3.36 MB), not zero.
Observer allocation is zero; no GC collections occurred in measured windows.

Raw results, frozen sources/DLLs, build logs and images remain separate in ignored
`Benchmarks.Local/tmp/tactics-v3-20261012`. [summarise.py](../../tools/TacticsStudy/summarise.py)
rotates already-built harnesses; [report.py](../../tools/TacticsStudy/report.py)
derives the tables and censored curves without simulation or result replacement.

## Runtime Checks

Full native Release validation, forced-scalar squad/scoring checks and all-five
3000-tick replays passed; ten checkpoints agree, final `1ABE850BB380646C`.
Contact/CCD, owner immunity, friendly/debris damage and lifecycle checks passed.
Twelve study-tool tests cover scheduling/conservation and censored reporting.

Desktop Release and production .NET 8 Mono AOT builds/run passed. The portable
WASM module is 8948900 bytes; decode checks found no SIMD instructions. Real
browser tests repeat all three seeds through 1200 ticks, checking identical
checkpoints/counts/counters, 2-4-member small-class same-team squads, common
targets and no self hits/errors. General three-profile controls/statistics,
responsive checks and 11 damage probes passed. Desktop/browser joint attacks
and damaged-member cover screenshots were inspected with colliders off;
native tick600 rendering/atlas/statistics capture and standard action-client
tick360 capture also passed. These are bounded runtime checks, not evidence
that every tactical situation or long endless wave is visually verified.

Only native SECS has old/new timing measurements here. WASM has actual AOT
correctness/runtime evidence, not an old/new performance claim. More seeds,
machines, long reinforcement battles, exact obstruction and mounted-gun state
remain follow-up validation limits. No commit, deployment or remote changes.
