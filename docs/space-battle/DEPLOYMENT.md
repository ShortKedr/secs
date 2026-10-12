English | [Русский](DEPLOYMENT.ru.md)

# Initial Fleet Deployment

Revision: `dispersed-front-v1`. Desktop and web use the same C# startup plan.

The old start sampled every escort in a strip only 32 units deep and 130 units
wide, without clearance checks. The new start uses a broader front with separate
depth: light four-ship packets, a rear escort screen and three separated carriers.
Packets are deployment positions, not forced AI groups. Existing tactics choose
their membership, targets and approach independently after the simulation starts.

At the default arena size, light packets use seven lanes 36 units apart and
24-unit depth steps. Ship slots are 12 units apart; even the largest escort
circles have at least 3.6 units of surface clearance. Carrier exclusion reserves
the carrier radius, maximum escort radius and four extra units. Opposing fleets
reflect X exactly; original birth order, home assignments, class counts, weapons,
physics, production and corner warp waves are unchanged. The placement itself
does not sample RNG. Seeds still control existing combat randomness.

This is a game deployment heuristic, not a model of real space warfare. It
creates maneuver room at tick zero, but does not constrain ships to remain in
their lanes or promise that later battles cannot become dense. Different initial
positions intentionally change trajectories and checksums, not weapon balance.

## Reproduction And Limits

`BattleConfig.Deployment` defaults to `LegacyRandom` in the core/headless host,
preserving existing benchmark inputs. Interactive hosts explicitly select
`Tactical`. Desktop `--legacy-deployment` restores the old start, including its
exact sequential RNG sampling. For example, deliberately dense stress runs use
`--ships 2048 --legacy-deployment`. Historical reports are not regenerated.

The startup planner allocates temporary arrays, not per-tick buffers. It searches
a bounded lattice (at most 4096 slots, planning half-size capped at 600), reserves
carrier clearance and assigns the nearest available slot in stable cell order.
It rejects fleets that do not fit rather than silently shrinking collision
geometry or spawning overlaps. Increasing arena size helps up to the planning
cap; extreme dense scenes remain available through the legacy configuration.

`SpaceBattle.Headless --validate-deployment` checks placement and repeated replay
on all five ECS adapters. `tools/web/check-deployment.mjs` verifies the production
browser deployment, live engagement, reset/replay and desktop/mobile captures.

For seed 7331 and the default light-swarm roster, the old initial frame has 65
overlapping circle pairs; the new frame has zero, with a minimum surface gap of
4.6 units. Fleet bounds grow from 204 x 130.04 to 376.8 x 232.5 units. Counts remain
198 ships and 52 mounted turrets. Chromium and desktop WebKit repeated low/nominal/high
and a second nominal seed through tick 900; each environment reproduces its own
checkpoints. Native Release/OpenGL captures verify the initial and engaged scenes.
Native/WASM bit-identical replay is not asserted. Historical legacy replay remains
`1ABE850BB380646C` at tick 3000 on all five native adapters.
