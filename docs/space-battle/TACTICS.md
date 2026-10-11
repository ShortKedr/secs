English | [Русский](TACTICS.ru.md)

[Methodology / README](README.md) | [Frozen report](REPORT.md)

# Tactical AI

This describes the implemented `tactical-lock-v2` in
[TacticalAi.cs](../../SpaceBattle.Core/TacticalAi.cs) and
[Battle.cs](../../SpaceBattle.Core/Battle.cs), with firing/repair rules in
[CombatSystems.cs](../../SpaceBattle.Core/CombatSystems.cs).
The report's measured workload is the frozen `predictive-neutral-v1`, not this
tactical revision. No new timing suite has been run for this AI; old charts are
not performance measurements of the new AI. This document makes no measured
speed or tactical-superiority claim.

## Deterministic Reasoning

`Collect` sorts ships by serial. Before individual decisions, `PrepareTactics`
copies living ships into immutable decision rows, reconstructs squads and assigns
shared targets. Strict score comparisons keep the first candidate on ties.
Tactical selection is a deterministic heuristic, not learning or a search planner;
the snapshots keep its reasoning independent of ECS query order. This does not
promise cross-platform floating-point/checksum equality.

Individual decisions normally occur every `12 + Serial % 8` ticks. Docked ships
and brains with `NextDecisionTick == int.MaxValue` skip decisions.

## Target Cost And Lease

Lower cost is preferred, among living undocked enemies:

```text
effectiveHealth = shield + hull / max(0.05, 1 - armor)
cost = (effectiveHealth / DPS + 1.8 * approachSeconds + 0.5 * flightSeconds)
       * (threatening ? 0.82 : 1)
```

- Primary and secondary DPS use damage, burst, cooldown, tick rate and churn.
  Each weapon uses a constant-velocity intercept with inherited shooter velocity
  and muzzle offset; missiles use speed 85 for this estimate. Outside touching
  distance, intercept times outside `(0, 4]` seconds give zero DPS. A radius/speed/
  flight-time accuracy weight is clamped to `[0.25, 1]`, with range attenuation.
- Combined DPS is scaled by cooling plus unused heat capacity spread over three
  seconds, divided by weapon heat rate, clamped to `[0.15, 1]`.
- Approach estimates closing to 90% of the larger weapon range using maximum
  speed minus the target's radial velocity. Targets already inside firing range
  need no approach. Contributions are rejected outside firing range when
  closing speed is at most 0.5, approach exceeds 15 seconds,
  or a carrier would chase beyond 1.5 times that range.
- Squads sum only `Attack` members' contributions, use the minimum approach time
  and maximum estimated flight time. A recent attacker (within 180 ticks), or an
  enemy in `Attack` targeting a contributor, receives the threat discount.

A changed nonempty target gets a 120-tick lease (two seconds at 60 Hz). A valid,
finite-cost target is retained during the lease. Afterwards, switching requires
a strictly greater than 35% cost reduction: `newCost < currentCost * 0.65`.
Dead, missing, docked or otherwise unavailable targets bypass the decision timer
and lease immediately at the next AI pass; nonfinite individual/group costs also
force reconsideration. Replacement can be empty when no feasible target exists.
A squad checks feasibility collectively, not separately for every member.
`EstimatedKillSeconds` stores this weighted cost, not a promised kill time.

## Small Squads And Movement

Undocked, decision-enabled `Interceptor`, `Fighter`, `Bomber` and `Gunship` in
`Attack` or `Screen` form same-team groups of 2-4. New members must be strictly
within 48 units of the leader; separation beyond 96 units, lost/ineligible leaders
or singleton groups trigger repair. Formation is revisited every 120 ticks or on
repair. Serial order and nearest candidates determine grouping, not optimal
assignment. Members receive the leader's shared focus target and lease.

Attacking members loosely follow the healthiest attacking member, using vertical
slot offsets; correction starts beyond 20 units and is capped at 40% maximum
speed. Orbital tangent speed is 65% maximum speed solo, 35% grouped, 15% for
carriers. Preferred distance is 48% of primary range, or 65% for carriers.
Facing follows the target in `Attack`, otherwise movement. Turning is smoothed:
desired angular speed is capped at 2.5 rad/s (carriers 0.8), with angular
acceleration limited to 10 rad/s² (carriers 1.5). Thrust, damping and arena-edge
steering still constrain movement.

## Screen And Repair Retreat

Threat detection uses an enemy that damaged the ship within 180 ticks and is
within 1.3 times its weapon range, or an attacking enemy targeting the ship within
1.1 times its range. A threatened squad member enters `Screen` below 65% hull
or 18% shield, if suitable cover exists.

Cover must be an undocked same-team ally outside `Screen`/`Retreat`/`Docking`,
with at least 65% hull and vitality at least 0.15 above the protected ship.
Vitality is `0.65 * hullFraction + 0.35 * shieldFraction`. Search range is
`48 + allyRadius`; scoring favors vitality, the same squad (+0.5), and proximity.
Overheated allies above 92% heat and threatened allies that qualify for their own
screening are excluded, preventing cover orders behind another retreating ship.
The ally need not belong to that squad. The destination is behind the ally away
from the threat, with clearance `allyRadius + ownRadius + 6`; a lateral waypoint
helps move around the ally when starting in front. This is steering, not a
guarantee of blocked enemy shots. `Screen` ships do not fire.
Screen steering matches the cover ally's pre-tick snapshot velocity and reduces position correction
near the destination, rather than crossing the point at full speed.

Recovery requires at least 75% hull, 35% shield and heat below 80% limit.
Until recovered, `Screen` persists while threatened or before the 90-tick cover
deadline; entering/changing cover resets that deadline. Losing usable cover can
return the ship to `Attack`; dead/docked cover bypasses the decision timer.

Critical retreat takes precedence for non-carriers with a home: hull below 25%
or heat above 92% limit. An existing retreat persists while hull is below 85%.
Ships head home and request docking within `homeRadius + ownRadius + 3`.
Docking still depends on lifecycle checks and repair-slot availability. Docked
ships repair hull, recharge shields and cool; undocking requires the release
tick, at least 98% hull and heat below 30% limit.

## Limits And Unchanged Rules

There is no obstacle pathfinding, fleet-level optimization or guaranteed cover.
Approximate kill cost does not forecast aim spread, occlusion, future maneuvering,
shield regeneration, consumable use, detailed missile acceleration/homing,
projectile caps, current cooldown readiness or future overkill allocation.
Intercept means an aiming estimate, not shooting down incoming projectiles.

Neutral projectile collision/friendly fire is unchanged: projectiles can damage
any enabled ship fixture, including allies and their owner; owner/target are not
team immunity. Actual predictive aim and deterministic spread remain firing
rules. See the frozen report for its original evidence and attribution limits,
not validation or performance results for this tactical revision.
