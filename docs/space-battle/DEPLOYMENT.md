English | [Русский](DEPLOYMENT.ru.md)

# Initial Fleet Deployment

Revision: `dispersed-front-v2`. Desktop and web use the same C# startup plan.

The old start sampled every escort in a strip only 32 units deep and 130 units
wide, without clearance checks. The new start uses a broader front with separate
depth: light four-ship packets, a rear escort screen and three separated carriers.
Packets are deployment positions, not forced AI groups. Existing tactics choose
their membership, targets and approach independently after the simulation starts.

Light packets retain broad approach lanes, but their centers, depth, orientation
and individual positions vary. Rear escorts and carriers also have staggered
positions. The actual formation no longer snaps to rows or repeats identical
squares. Clearance checks reserve at least 2.4 units between escort surfaces and
four units around carriers. These are deployment spacing choices, not changes to
collision geometry. Opposing fleets reflect X exactly; original birth order,
home assignments, class counts, weapons,
physics, production and corner warp waves are unchanged. Placement uses its own
`RandomStream(Seed ^ 0xD1B54A35)`, without advancing the combat/spawn RNG. The same
seed repeats the same layout; different seeds also change initial positions.

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

The startup planner allocates temporary arrays, not per-tick buffers. It first
reserves carrier clearance and assigns nearest slots in stable cell order on a
bounded safety lattice (12-unit spacing, at most 4096 slots, planning half-size
capped at 600). It then tries up to twelve off-grid displacements per escort,
checking each against every actual hull. A safe lattice position remains the
fallback if all attempts fail; very crowded starts can therefore be more regular.
These bounded all-hull checks cost O(S²) at startup, never on each simulation tick.
It rejects fleets that do not fit rather than silently shrinking collision
geometry or spawning overlaps. Increasing arena size helps up to the planning
cap; extreme dense scenes remain available through the legacy configuration.

`SpaceBattle.Headless --validate-deployment` checks placement and repeated replay
on all five ECS adapters. `tools/web/check-deployment.mjs` verifies the production
browser deployment, live engagement, reset/replay and desktop/mobile captures.

Historical v1 verification, before the natural-layout revision: for seed 7331 and
the default light-swarm roster, the legacy initial frame had 65 overlapping circle
pairs; v1 had zero, with a minimum surface gap of
4.6 units. Fleet bounds grow from 204 x 130.04 to 376.8 x 232.5 units. Counts remain
198 ships and 52 mounted turrets. Chromium and desktop WebKit repeated low/nominal/high
and a second nominal seed through tick 900; each environment reproduces its own
checkpoints. Native Release/OpenGL captures verify the initial and engaged scenes.
These v1 measurements are not measurements of v2. Native/WASM bit-identical replay
is not asserted. Historical legacy replay remains
`1ABE850BB380646C` at tick 3000 on all five native adapters.

V2 verification for the same default roster and seed 7331: zero initial overlaps,
minimum observed surface gap 2.629 units, bounds 383.87 x 255.12, and the same 198
ships / 52 turrets. Native Release geometry, irregularity and replay checks pass
on all five adapters. Chromium and desktop WebKit each pass eight reset runs
(low/nominal/high plus nominal seed 27, two repeats, ticks 0/120/360/900), including
unchanged initial attributes, actual combat and zero self-hits. Desktop OpenGL,
browser desktop/mobile and the standard action-client captures were inspected.
