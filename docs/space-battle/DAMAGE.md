English | [Русский](DAMAGE.ru.md)

# Layered Weapon Damage

Current source revision: **`layered-weapon-damage-v1`**. Desktop and production
Mono AOT WASM use the same [C# rules](../../SpaceBattle.Core/DamageRules.cs).
This intentional gameplay change invalidates older trajectories/checksums; it
does not revise historical measurements or claim a performance improvement.

## Profiles

| Weapon | Shield multiplier | Hull multiplier |
| --- | ---: | ---: |
| Plasma | 1.5 | 1 |
| MachineGun, Cannon, HeavyCannon | 1 | 1.5 |
| Missile | 1 | 1 |
| Collisions and debris | 1 | 1 |

The 1.5 specialization is **initial tuning, not validated balance**. No weapon
base damage, cadence, heat limit, projectile geometry, faction neutrality or
owner immunity changes. Mounted rounds use their actual gun's weapon kind.
Hull armor is the existing fractional reduction; no new armor classes exist.

`BattleConfig.Damage` holds immutable settings. Coefficients must be finite and
positive. `DamageRules.ForWeapon` is the only weapon-to-profile mapping. For example:

```csharp
var config = new BattleConfig {
    Damage = new DamageSettings { Missile = new DamageProfile(1.2f, 1.1f) }
};
```

The existing plasma shield-to-heat rate remains 1.8 and is not a balance knob in
this change. To adjust plasma specialization, use `new DamageSettings().Plasma
with { ShieldMultiplier = 1.6f }`, retaining that heat field.

## One Hit

For base damage `D`, current shield `S`, shield multiplier `Ms` and hull multiplier `Mh`:

1. Spend `min(D, S / Ms)` base damage on the shield.
2. Remove the spent base damage times `Ms` from the shield.
3. Pass only the remaining base damage into the hull.
4. Hull damage is `remaining * Mh * (1 - armor)`.

Removed shield is clamped to available shield solely to avoid a floating-point
round-trip overshoot. Plasma adds `actual removed shield * 1.8` heat, never heat
for hull damage. Damage still queues the existing combat/shield-break/death states.
The legacy `Damage(..., plasma: true, ...)` uses the plasma profile; false uses
neutral damage. `DamageWeapon` is the explicit weapon API used by all projectile hits.

| Base 100, armor 0.2 | Shield before | Shield removed | Hull damage | Plasma heat |
| --- | ---: | ---: | ---: | ---: |
| Plasma | 75 | 75 | 40 | 135 |
| Kinetic | 75 | 75 | 30 | 0 |
| Missile | 75 | 75 | 20 | 0 |
| Plasma | 0 | 0 | 80 | 0 |
| Kinetic | 0 | 0 | 120 | 0 |

## AI Estimate

This section records the initial v2 layer estimate. Current source uses
[layered-squads-v3](TACTICS_V3.md) with readiness, staggered first impacts,
separate suppression/loss estimates and usefulness-based composition. Damage profiles are unchanged.

The AI sums each contributing weapon's expected DPS separately for shield and
hull, including its layer profile, accuracy/range and heat budget. Its continuous
two-phase estimate is `S / shieldDps + H / (hullDps * (1 - armor))`, skipping empty
layers and treating a positive layer with no effective DPS as unreachable.
It does not divide one effective-health scalar by mixed raw DPS. Existing
approach time, flight time, threat preference, target locks and group formation remain.

This is an approximation: discrete volleys/overkill, future shield regeneration,
heat evolution, exact arrival schedules, turret aim/obstruction and asynchronous
target choices are not simulated by the estimate. Mounted DPS retains the existing
class-catalog cannon/plasma pairs and independent cooling budget: arbitrary external
gun edits, removal or reparenting are not represented in that approximation. Actual
hits always use the emitted projectile's own weapon profile. The estimate is a selection
heuristic, not a promise of measured time to kill.

## Verification

Three fresh native processes produced identical 24-row controlled studies.
Each volley schedules base damage 100, starting at tick 60 and repeating every
60 ticks (60 Hz). Target hull is 1000, armor 0.25, and only the starting shield
changes. The mixed pair schedules 50 plasma + 50 cannon base damage; all-five
schedules 20 per kind. Results below are **simulation seconds to kill**, not CPU time.

| Composition | Shield 0 | Shield 750 | Shield 3000 |
| --- | ---: | ---: | ---: |
| MachineGun / Cannon / HeavyCannon | 9 | 17 | 39 |
| Plasma | 14 | 19 | 34 |
| Missile | 14 | 21 | 44 |
| Plasma then Cannon | 11 | 17 | 35 |
| Cannon then Plasma | 11 | 17 | 35 |
| All five | 11 | 18 | 38 |

These synthetic ordered hits isolate layer accounting: no travel, misses,
regeneration, cooling, heat lockout, AI, or secondary fragments. Actual weapon
catalogs are untouched. Plasma gains on the shield-heavy fixture, but kinetic
still wins at shield 750 because hull work dominates. This is not fleet balance.
Raw evidence: `Benchmarks.Local/tmp/weapon-damage-20261012/compositions-r1.json`
through `compositions-r3.json` (ignored, not published).

Native full validation and ten checkpoints through tick 3000 passed on all five
adapters, with final checksum `8C4682204BBAC537` in that native scenario. Forced
scalar damage/AI checks passed. Warmed direct `DamageWeapon` batches allocated
zero bytes on all five stores; this is not a claim about whole-Step allocations.

Windows x64 desktop Release and stable .NET 8.0.31 production Mono AOT were built
and run. Chromium passed 11 actual AOT layer probes, three profile/reset replays,
combat, controls and desktop/mobile rendering checks. Desktop controls, counts,
statistics and nonblank atlas rendering passed at tick 600. The unmodified
Playwright action client reached tick 360 with no errors. Desktop, browser and
mobile frames were visually inspected, colliders disabled. No cross-platform
bitwise checksum guarantee or validation on other browser/CPU architectures is claimed.

Run the [headless checks](../../SpaceBattle.Headless/Validation.cs) with
`--validate`, `--validate-adapters` and `--validate-ports`. The controlled
`--damage-study` compares equal base-DPS compositions and shielded/unshielded
targets, without attributing differences to fleet balance. Save new evidence
under ignored `Benchmarks.Local/tmp`; never replace published benchmark data.

The production [browser checks](../../tools/web/check-web.mjs) inspect the actual
AOT layer outputs and revision as well as combat, deterministic reset and rendering.
Revision metadata is exported by both frontends and new headless reports.
