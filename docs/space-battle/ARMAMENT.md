English | [Русский](ARMAMENT.ru.md)

[Current tactics](TACTICS.md) | [Historical benchmark](REPORT.md)

# Mounted Armament

Gameplay revision: `mounted-armament-v3`. Frozen benchmark measurements remain
`predictive-neutral-v1`; they do not measure tactical AI or these guns.

| Class | Turret entities | Main damage | Main cooldown, seconds | Main range |
|---|---:|---:|---:|---:|
| Frigate | 2 | 160 | 3.5 | 170 |
| Destroyer | 2 | 240 | 4 | 170 |
| Light carrier | 4 | 480 | 6 | 220 |
| Fleet carrier | 6 | 640 | 7 | 220 |
| Foundry carrier | 8 | 800 | 8 | 220 |

Nominal cooldowns assume 60 ticks/s; low/high profiles scale them with the
existing churn multiplier. These are initial tuning values, not proven balance.
Small classes and Support keep their weapons. Platforms replace their old
primary/secondary pair with the main cannon and attached guns.

## Independent Guns

Each turret has `MountedTurret`, `Transform`, `Weapon` and `Heat`. Paired mounts
alternate ordinary cannon (28 damage, 42-tick cooldown) and plasma (13 damage,
18-tick cooldown). Range is 120 for escorts, 150 for carriers. Heat capacity is
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

All guns retain deterministic 75/25 precise/spread decisions. Mounted rounds
inherit parent linear velocity plus tangential mount velocity. Muzzles launch
outside the enclosing hull circle without collision immunity. Projectile owner
is the parent for damage/threat attribution; friendly, enemy and self hits remain
possible. Only attacking, undocked, non-overheated parents permit turret fire.
Turret heat is independent of the main gun; cooling continues while idle.
Docked mounts are hidden; retreating and screening platforms do not fire.

Target reasoning includes mount DPS with separate heat budgets. It is a
heuristic, not an exact cooldown/turning-time solver. Sorted shots and checksummed
mount state preserve adapter-independent replays, not cross-platform float parity.

Sources: [catalog](../../SpaceBattle.Core/Components.cs),
[mount systems](../../SpaceBattle.Core/ArmamentSystems.cs),
[combat](../../SpaceBattle.Core/CombatSystems.cs),
[tests](../../SpaceBattle.Headless/ArmamentValidation.cs).

Validation covers independent targets/heat/cooldowns, rotating attachments,
inherited projectile velocity, neutral damage attribution, hull alignment,
heavy damage/repeat timing, dock visibility and parent-death cleanup. All five
adapters matched 10 checkpoints over 3000 ticks (final checksum
`DC7DFAFE4769F423`). This is a correctness replay, not a timing benchmark.
The [browser armament check](../../tools/web/check-armament.mjs) verifies actual
AOT heavy-round travel and independent swivel, with close desktop/mobile captures.
