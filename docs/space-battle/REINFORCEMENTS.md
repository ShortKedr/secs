English | [Русский](REINFORCEMENTS.ru.md)

[Scenario](README.md) | [Weapons](ARMAMENT.md) | [Web](../../SpaceBattle.Web/README.md)

# Corner Warp Reinforcements

The interactive desktop and web simulations enable `corner-warp-v1` by default.
A depleted fleet receives new forces through a corner warp, rather than reviving
destroyed carriers at their former positions. Surviving ships keep their entities,
health, targets and positions. Ordinary carrier production continues.

## Trigger And Limits

Every 60 ticks, each team compares its remaining ship count and effective strength
with its starting fleet. A wave is scheduled if either falls to 25% or lower and
there is a free population slot. Effective strength weights ship classes and
uses 65% hull health plus 35% shield health. A damaged but numerous fleet can
therefore request help. The trigger is not based only on the number of carriers.

| Setting | Default | Purpose |
| --- | ---: | --- |
| Check interval | 60 ticks / 1 s | Avoid per-frame planning |
| Depletion threshold | 25% | Help significantly depleted fleets |
| Wave size | 65% of initial ship cap | Reinforce without resetting the whole fleet |
| Warp warning | 90 ticks / 1.5 s | Visible arrival lead-in |
| Arrival interval | 4 ticks | At most one ship per team per interval |
| Cooldown after a wave | 600 ticks / 10 s | Prevent repeated requests every tick |
| Closing effect | 45 ticks / 0.75 s | Settle the aperture after arrival |

These are configurable gameplay defaults, not experimentally proven balance.
The population cap remains `ShipsPerTeam + CarriersPerTeam` for each team;
nominal is 99. Carrier count never exceeds `CarriersPerTeam`. Only one wave per
team may be pending. If all candidate locations remain blocked, the wave ends
after its bounded retry deadline and retries from another corner after cooldown.

Strength weights are 0.75/1/1.5/2 for interceptor/fighter/bomber/gunship,
3/4/6/2 for corvette/frigate/destroyer/support, and 12/18/24 for the three
carrier classes. Zero-capacity shields contribute zero shield strength.

## Arrival

Seed, team and wave number select a rotating corner and one of four compositions:
fast raiders, armored escorts, bomber groups or mixed patrols. Successive waves
vary their escort mix. If the side has no carriers, the wave includes a new one;
some compositions restore two. These are new ships, not copies of destroyed IDs.

Interactive clients now use the [light-swarm compositions](FLEETS.md): all four
variants favor small ships, with different bomber/raider/screening weights.
The original equal/armored mixtures remain available in the Core's Uniform mode.

The aperture opens 44 world units inside an arena corner. New ships materialize
within 28 units of it, facing and moving inward. Up to 32 local candidate positions
are checked per arrival using the physics broadphase and conservative hull circles.
Occupied positions defer arrival instead of spawning overlapping hulls. The wave
cannot exceed the population cap, even if normal production filled slots meanwhile.

Portals use cyan or red artwork for the receiving team; each arriving ship has a
separate materialization flash. Shared C# presentation controls opening, pulse,
closing and flash fade, while both clients use the same
[generated warp atlas](../../SpaceBattle.Game/Assets/PROVENANCE.md#warp-atlas).
Portal particles stay below ships; arrival flashes stay above them. Particle caps
and disabled effects never suppress simulation arrivals. There is no warp damage,
invulnerability, full-field exposure flash or camera shake.

## Scope And Checks

`BattleConfig.EndlessReinforcements` is opt-in in the shared Core and enabled by the
interactive hosts. Desktop `--finite` disables this endless rule. Headless benchmark
configuration and historical measurements are not silently changed. Gameplay wave
state and warp particles participate in deterministic checksums.

The [headless tests](../../SpaceBattle.Headless/WarpReinforcementValidation.cs) cover
thresholds, timing, survivors, team/corner ownership, varied compositions, caps,
blocked placement, cooldown, no per-carrier revival in endless mode, normal
production, effects and deterministic replay across all five ECS ports.
The [browser check](../../tools/web/check-warp-reinforcements.mjs) exercises actual
simulation arrivals, atlas pixels, effect lifetime and fixed brightness budgets.
