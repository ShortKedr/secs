English | [Русский](FLEETS.ru.md)

# Fleet Composition

[Desktop](../../SpaceBattle.Game/README.md) | [Web](../../SpaceBattle.Web/README.md) | [Warp reinforcements](REINFORCEMENTS.md)

Interactive clients use `FleetComposition.LightSwarm`, revision `light-swarm-v1`.
The smallest rapid-fire ship, Interceptor, is the bulk of the fleet. Large hulls
remain anchors surrounded by expendable escorts, rather than equally numerous
ship classes. This is a composition change, not a weapon or durability buff.

## Initial Fleet

Counts per team, with the default 96 escorts and three carriers:

| Class | Uniform | Light Swarm |
| --- | ---: | ---: |
| Interceptor | 12 | 54 |
| Fighter | 12 | 18 |
| Bomber | 12 | 6 |
| Gunship | 12 | 6 |
| Corvette | 12 | 6 |
| Frigate | 12 | 2 |
| Destroyer | 12 | 2 |
| Support | 12 | 2 |
| LightCarrier | 1 | 1 |
| FleetCarrier | 1 | 1 |
| FoundryCarrier | 1 | 1 |
| Total | 99 | 99 |

Interceptors increase 4.5 times; Interceptors plus Fighters make up 75% of
escorts. Interceptors keep their existing 2.5-damage MachineGun, two-shot burst
and five-tick nominal cooldown. Fighters retain their cannon: small hull does
not imply identical weapons. No damage, shield, armor, heat or cooldown value
is changed. Carrier guns still use the separately documented armament tuning.

The [shared roster](../../SpaceBattle.Core/FleetCompositionRules.cs) caches a
48-ship smooth weighted round-robin cycle. Rare escorts are interleaved with
small ships, with no additional RNG consumption or per-spawn roster allocations.
Other population sizes take a prefix/repetition of this cycle; tiny fleets
need not contain every rare class. Both teams receive the same class schedule.

## Production And Waves

Carrier production repeats the initial roster by its successful build ordinal;
blocked/full-cap attempts do not advance it. Build intervals, capacities and
per-team population caps are unchanged. Normal production therefore does not
gradually replace the swarm with an equal-class fleet.

The four rotating warp compositions use these weights per 48 escorts:

| Variant | Interceptor | Fighter | Bomber | Gunship | Corvette | Frigate | Destroyer | Support |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Standard | 27 | 9 | 3 | 3 | 3 | 1 | 1 | 1 |
| Bomber Escort | 24 | 9 | 6 | 3 | 3 | 1 | 1 | 1 |
| Fast Raiders | 30 | 9 | 3 | 2 | 2 | 0 | 1 | 1 |
| Screening Force | 26 | 10 | 2 | 4 | 3 | 1 | 1 | 1 |

Carrier restoration still reserves its existing one/two wave slots first;
remaining slots follow the selected escort cycle. Wave warning, arrival rate,
placement checks, strength thresholds, cooldown and caps are unchanged.

## Scope And Validation

Shared Core defaults to `FleetComposition.Uniform`. Headless benchmarks and
historical measurements keep their equal-class workload. Desktop `--finite`
disables waves, not its new initial/production roster. Current gameplay hashes
identify the non-default composition; legacy finite hashes remain unchanged.

The [headless checks](../../SpaceBattle.Headless/FleetCompositionValidation.cs)
cover weighted cycles, initial counts, production, varied reinforcements, caps
and deterministic replay across ECS adapters. The [browser checks](../../tools/web/check-web.mjs)
verify the real AOT initial roster, combat, resets and controls. This is initial
spectacle-oriented tuning, not proof of matchup balance or browser frame budgets:
more rapid-fire ships can create more projectiles even at the same ship cap.
