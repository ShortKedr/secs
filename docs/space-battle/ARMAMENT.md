English | [Русский](ARMAMENT.ru.md)

[Current tactics](TACTICS_V3.md) | [Current benchmark](REPORT.md)

# Mounted Armament

Gameplay revision: `mounted-armament-v7`. Current AI is [layered-squads-v3](TACTICS_V3.md).
The [2026-10-11 measurements](REPORT_2026-10-11.md) used `predictive-neutral-v1`
and do not measure these guns; see the [current report](REPORT.md) for current-source measurements.

| Class | Turret entities | Main damage | Main cooldown, seconds | Main range |
|---|---:|---:|---:|---:|
| Frigate | 2 | 60 | 12 | 170 |
| Destroyer | 2 | 80 | 14 | 170 |
| Light carrier | 4 | 480 | 18 | 220 |
| Fleet carrier | 6 | 640 | 22 | 220 |
| Foundry carrier | 8 | 800 | 26 | 220 |

Nominal cooldowns assume 60 ticks/s; low/high profiles scale them with the
existing churn multiplier: low uses 24/28/36/44/52 seconds, high uses
6/7/9/11/13 seconds, in table order. These are initial tuning values, not proven balance.
Small classes and Support keep their weapons. Platforms replace their old
primary/secondary pair with the main cannon and attached guns.

Revision v5 changes heavy cadence, not per-shot lethality: damage stays
60/80/480/640/800, including the escort reduction introduced in v4. Range,
projectile type/speed, heat cost and normal guns are unchanged. Sustained heavy
firepower and AI estimated DPS change with the catalog rate; this is not a
balance proof. Previously the nominal initial fleet of 198 ships had 54 ready
heavy weapons, with cooldowns of 3.5/4/6/7/8 seconds in table order.

`SpawnShip` assigns a deterministic initial heavy charge delay of 50%-100% of
the actual churn-scaled cooldown. A local RNG derived from battle seed and ship
serial is independent of the battle RNG and does not consume its draws. Readiness
is relative to `birthTick`, including late-built and respawned ships: no free
first heavy volley. Turrets start ready as before. Subsequent heavy shots use
the full scaled cooldown. At the V5 cadence change, tactical revision was `tactical-lock-v2`; only
catalog rate changes its estimated DPS, not the tactical algorithm.

## Independent Guns

Revision v7 disables projectile self hits for every weapon kind, including
mounted guns and heavy hull cannons. The swept collision query excludes every
fixture of the projectile's owner, including start-point overlaps. In the Aether implementation used when v7 was introduced, this exclusion also covered all five offset rays; current [circle physics](PHYSICS.md) uses analytic sweeps. The query continues searching for the nearest other ship: an owner cannot block
a shot aimed past it. Mounted projectiles use the parent ship as owner, not the
turret entity. Lifetime-safe ECS keys resolve a live owner to its unique ship
serial; recycled body/entity slots do not give another ship immunity.
Friendly fire is retained. Debris damage and physical hull contacts are unchanged.
The `SelfHits` telemetry field stays present for compatibility and remains zero.
Weapon damage/cadence, aiming, spread and launch positions retain v6 settings.

Revision v6 makes mounts auxiliary weapons: damage is one quarter of the
corresponding Fighter/Bomber gun and cooldown is doubled. This reduces nominal
raw sustained battery DPS by 87.5%, and the opening volley damage by 75%.
Main/heavy weapons, mount counts, range, ballistics, heat and targeting rules
are unchanged. AI kill-time reasoning reads the reduced catalog damage/rate.
This is an initial balance adjustment, not proof of matchup balance.

| Class | Battery DPS, v5 | Battery DPS, v6 |
|---|---:|---:|
| Frigate / Destroyer | 83.33 | 10.42 |
| Light carrier | 166.67 | 20.83 |
| Fleet carrier | 250.00 | 31.25 |
| Foundry carrier | 333.33 | 41.67 |

These are raw `sum(damage * burst * 60 / cooldown)` values at nominal churn,
before aim, armor, shields, heat, range or target availability. Low/high profiles
retain their existing cooldown scaling. DPS is not measured damage delivered.

Both frontends render the guns using a dedicated [generated gun atlas](../../SpaceBattle.Game/Assets/PROVENANCE.md#gun-atlas).
Fixed pedestals and independently rotating heads have measured bearing pivots;
the existing recoil/heat presentation is retained. The heavy hull cannon has
its own larger sprite. This is a visual change only, not a gameplay revision.

Each turret has `MountedTurret`, `Transform`, `Weapon` and `Heat`. Paired mounts
use the parent hull's team RGB multiplier for both their base and weapon head;
the main heavy cannon uses the same paint. Geometry, recoil and heat cues are
unchanged. Browser hulls and guns cache both native palette variants at load.

Paired mounts
alternate auxiliary cannon (7 damage, 84-tick cooldown = 1.4 seconds nominal)
and plasma (3.25 damage, 36-tick cooldown = 0.6 seconds nominal).
Range is 120 for escorts, 150 for carriers. Heat capacity is
60, cooling 12/s, shot heat half the ordinary weapon's cost. They have no extra
physics body or independently damageable hull. Parent destruction removes all
mounts in the same lifecycle flush. Statistics include docked mounts separately.

Mounts follow parent translation/rotation. Each gun swivels independently at
5 rad/s, retaining its living, undocked in-range enemy. Reacquisition happens at
staggered `30 + slot` tick intervals: parent focus first, otherwise nearest enemy
with serial-order ties. Searches do not run every tick for every mount.

Aim tolerance is 0.08 rad for turrets and 0.12 rad for the hull cannon. Heavy
weapon steering follows predicted intercept. Its projectile speed is 170 and
radius 0.9; it is not hitscan or a guaranteed kill. Cyan impact effects do not
introduce area damage.

Every actual heavy-round collision produces a separate cyan contact blast,
including nonlethal hits: one stationary core at the swept impact point and
smaller dispersing light fragments. It expands over 6 ticks, holds full opacity
for 3 ticks, then fades within 36 ticks (0.6 seconds). Core radius scales with
shot damage, clamped to 3.6-7.2 world units before presentation expansion.
Both clients draw this effect above hulls and mounts; orange kind-2 ship
destruction stays separate. Misses, expiry and owner-excluded crossings do not
trigger it. The existing 12-particle base budget, multiplier and particle cap
remain in force; there is no added splash, debris, damage or camera shake.
Particle state/checksums can change; weapon balance and archived measurements
are not revised. [Heavy-impact checks](../../tools/web/check-heavy-impact.mjs)
capture every tick of a real hit's lifetime and apply the existing contrast
budgets; headless checks cover surviving/lethal hits and all five ECS ports.

All guns retain deterministic 75/25 precise/spread decisions. Mounted rounds
inherit parent linear velocity plus tangential mount velocity. Muzzles launch
outside the enclosing hull circle. Projectile owner is the parent for damage/
threat attribution and self-collision exclusion; other friendly and enemy hits
remain possible. Only attacking, undocked, non-overheated parents permit turret fire.
Turret heat is independent of the main gun; cooling continues while idle.
Docked mounts are hidden; retreating and screening platforms do not fire.

Target reasoning includes mount DPS with separate heat budgets. It is a
heuristic, not an exact cooldown/turning-time solver. Sorted shots and checksummed
mount state preserve adapter-independent replays, not cross-platform float parity.

Sources: [catalog](../../SpaceBattle.Core/Components.cs),
[mount systems](../../SpaceBattle.Core/ArmamentSystems.cs),
[combat](../../SpaceBattle.Core/CombatSystems.cs),
[tests](../../SpaceBattle.Headless/ArmamentValidation.cs),
[collision filtering](../../SpaceBattle.Core/PhysicsService.cs),
[owner regressions](../../SpaceBattle.Headless/ProjectileOwnerValidation.cs).

V7 validation passed 6,000 owner-filtered/reference comparisons and subsequent
neutral queries, including compound starts, offset rays, disabled fixtures and
recycled body slots. Real projectile tests cover all five weapon kinds, parent
ownership after turret reparenting, unchanged expiry, friendly fire, neutral
debris and dead/stale owner generations across all five stores. All five adapters
matched 10 checkpoints over 3000 ticks, with zero self hits at every checkpoint
(checksum `0D9F42BC28101A28`). Native render/controls and production AOT checks
passed; all three WASM profiles had zero self hits through tick 1800, while
friendly hits continued. These are bounded correctness checks, not benchmarks.

Validation covers seeded spawn-charge bounds, late births, exact first-shot
deadlines and regular turret fire during heavy charge, as well as independent
targets/heat/cooldowns, rotating attachments,
inherited projectile velocity, neutral damage attribution, hull alignment,
heavy damage/repeat timing, dock visibility and parent-death cleanup.
V6 also checks one-eighth raw battery DPS for all five platform classes and
three real catalog-turret shots/impacts at exact deadlines for both gun types
under low/nominal/high churn, with deterministic repeat scenes. All five
adapters matched 10 checkpoints over 3000 ticks in v6 (final checksum
`0D9F42BC28101A28`). Native render/controls and production AOT combat, all
profiles, reset, statistics, controls, gun pixels/swivel and heavy travel passed.
These verify implementation, not matchup balance or performance.

Historically, all five
adapters matched 10 checkpoints over 3000 ticks in v5 (final checksum
`F42899B97415194A`). This is a correctness replay, not a timing benchmark.
The earlier checksum `978EE44E84D87355` describes historical v4 gameplay.
Impact tests compare the same damaged, unshielded Frigate:
escort heavy rounds leave it alive, while carrier heavy rounds destroy it.
The [browser armament check](../../tools/web/check-armament.mjs) verifies actual
AOT heavy-round travel and independent swivel, with close desktop/mobile captures.

The [opening cadence check](../../tools/web/check-heavy-cadence.mjs) exercises
all three production AOT profiles, ordinary combat during initial charge,
eventual heavy fire and exact reset replays. In the bounded nominal seed 7331
comparison, v4/v5 heavy-shot totals were 79/0 at tick 300, 91/1 at tick 600 and
131/16 at tick 1800. These count shots, not hits or kills; they are not independent
timing repeats or a balance proof. Ordinary combat can still destroy small ships
quickly. Frozen benchmark evidence was not replaced.
