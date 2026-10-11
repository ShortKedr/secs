English | [Русский](SYSTEMS.ru.md)

[Report](REPORT.md) | [Project README](../../README.md)

# SpaceBattle Systems and Components

This catalogue describes the current implementation from source code, without measuring performance or access frequency. A component here means an independent value attached to an entity through `IEcsStore.Set<T>`, not every field, nested structure, or type registered in an adapter.

Primary sources: [Battle.cs](../../SpaceBattle.Core/Battle.cs) (`StageNames`, `Step`, `Collect`, `SpawnShip` and processors), [CombatSystems.cs](../../SpaceBattle.Core/CombatSystems.cs) (weapons, carriers, movement, hits and lifecycle), [Components.cs](../../SpaceBattle.Core/Components.cs) (types and ship catalogue), [EcsContract.cs](../../SpaceBattle.Core/EcsContract.cs) (storage contract).

## Nine Timed Stages

`Battle.Step()` increments `Tick` and executes **9 stages strictly in the order below**. These are nine `LastStageMs` intervals, not nine system classes: individual intervals contain multiple processors and helper operations. `lifecycle` timing includes the final `Commit()` and repeated `Collect()`.

In the tables, `A + B` means a query for entities with both components. Additional `Get<T>` and `Has<T>` inside a processor do not expand the query filter. Passing a component by `ref` does not itself mean the processor modifies it. "Write" below means actual field changes; creation, destruction and changes to the component set are listed separately.

| No. | Stage name | Execution and responsibility | Queries / traversal source |
| --- | --- | --- | --- |
| 1 | `collect` | `Collect`: refreshes ship, projectile, debris and particle lists; computes team counts and sorts lists by `Serial`. A ship row contains copies of identity and transform, and normalized hull/shield values. | `ShipCollector`: `Identity + Transform`; `ProjectileCollector`: `Projectile`; `DebrisCollector`: `Debris`; `ParticleCollector`: `Particle`. |
| 2 | `ai` | `AiSystem`: at decision intervals selects the nearest living undocked enemy, updates the home carrier, switches attack/retreat; requests docking when approaching home. Docked ships are skipped. | `Identity + Brain + Transform`. |
| 3 | `thermal-inventory` | First `ThermalSystem`: cooling, shield regeneration, overheat and state-expiry checks. Then `InventorySystem`: uses one suitable consumable when the inventory is ready. | `Identity + Heat + Shield`; then `Identity + Inventory + Hull`. |
| 4 | `steering` | `SteeringSystem`: heading home or orbiting a target, returning within arena bounds, acceleration, speed limiting and engine heat. Zeroes velocity and acceleration for docked ships. | `Transform + Motion + Brain`. |
| 5 | `weapons` | `WeaponSystem`, then `SecondarySystem`: attack, range, readiness and heat checks; generates requests. `FlushShots`: sorts requests by serial and slot, calculates intercept aim and spread, creates burst projectiles and heats the shooter. | `Identity + Weapon + Brain`; `Identity + SecondaryWeapon + Brain`; then the `shots` list. |
| 6 | `carriers` | `CarrierSystem`: schedules escort construction when the timer and team limit allow. `DockSystem`: moves a docked ship to its carrier, repairs, recharges shields and cools it; requests undocking when ready or when the carrier is lost. | `Identity + Carrier + Transform`; `Identity + Docked + Hull`. |
| 7 | `physics` | `StepPhysics`: synchronizes ships with the external physics world, steps it and transfers results into ECS. Then moves projectiles (with missile homing), debris and particles. | The `ships` list; then `Transform + Motion + Projectile`, `Transform + Motion + Debris`, `Transform + Motion + Particle`. |
| 8 | `hits` | `ResolveHits`: physical ship-contact damage, projectile/debris sweep checks, hit effects; marks expired/impacting objects for retirement. `Damage` updates shield, hull, heat for plasma and the combat timestamp. | `physics.Contacts` contacts, `projectiles`, `debris`, `particles` lists; ship access through `serialToShip`. |
| 9 | `lifecycle` | `FlushLifecycle`: sorts and applies state requests, docking/undocking, ship destruction, transient-object retirement, construction and deferred carrier replenishment. Then `Step` performs `Commit` and `Collect`. | `states`, `retired`, `builds`, `reinforcements` lists; `ships` is used to check dock occupancy and escort ownership; final queries are as in `collect`. |

### Component Access and Structural Effects

The main direct accesses are listed, including access to targets and home carriers. `AliveShip` checks read `Identity` (presence) and `Hull`; `State` reads `Identity.Serial`. Collections, queues, counters and physics state are not ECS components.

| Stage | Main reads / presence checks | Field writes | Structural action |
| --- | --- | --- | --- |
| `collect` | `Identity`, `Transform`, `Hull`, `Shield`, `Projectile`, `Debris`, `Particle`. | No component writes. | None; external working lists are refreshed. |
| `ai` | `Identity`, `Transform`, `Brain`, `Hull`, `Heat`, `Collider`; `Docked` presence, target/home liveness; snapshots from `ships`. | `Brain.Target`, `Home`, `Mode`, `NextDecisionTick`. | Only a `Dock` request; the component appears in `lifecycle` if checks pass. |
| `thermal-inventory` | `Identity`, `Heat`, `Shield`, `Inventory`, `Hull`; presence/fields of `Overheated`, `ShieldBroken`, `InCombat`, `Boosting`. | `Heat.Value`, `Shield.Current`, `Hull.Current`; item count inside `Inventory`, `Inventory.NextUseTick`. | Requests to add/remove `Overheated`, remove `ShieldBroken`, `InCombat`, `Boosting`. Consuming an item does not remove the inventory component. |
| `steering` | `Transform`, `Motion`, `Brain`, `Weapon`, `Identity`, `Heat`; presence of `Docked`, `Boosting`, `Overheated`; target/home transforms and liveness. | `Motion.Velocity`, `Acceleration`, `AngularVelocity`; `Heat.Value`. `Transform` itself is not changed here. | A `Boosting` request; no immediate composition change. |
| `weapons` | `Identity`, `Brain`, `Weapon` / `SecondaryWeapon.Value`, `Heat`, `Transform`, `Motion`, `Collider`; presence of `Docked`, `Overheated`, target liveness. | `Weapon.NextFireTick` / `SecondaryWeapon.Value.NextFireTick`; shooter's `Heat.Value`; initial components of new projectiles. | `FlushShots` creates entities with `Transform + Motion + Projectile`; an `Overheated` request is possible; `Store.Commit()` is called after the bursts. |
| `carriers` | `Identity`, `Carrier`, `Transform`, `Docked`, `Hull`, `Shield`, `Heat`; carrier liveness, team-count snapshot. | `Carrier.NextBuildTick`; docked ship's `Transform.Previous/Position`, `Hull.Current`, `Shield.Current`, `Heat.Value`. | Only construction and `Undock` requests; actual creation and `Docked` removal occur in `lifecycle`. |
| `physics` | `PhysicsLink`, `Docked`, `Transform`, `Motion`; `Projectile` and missile target transform/liveness; `Debris`, `Particle` are in filters, but movement processors do not use their fields. | `Transform.Previous/Position/Angle` (set depends on object type), ships' `Motion.Velocity/AngularVelocity`, missiles' `Motion.Acceleration/Velocity`. | No changes to component sets. External bodies of docked ships are disabled, not removed. |
| `hits` | `Projectile`, `Debris`, `Particle`, `Transform`, `Motion`; `Identity` for liveness/team checks; ship's `Hull`, `Shield`, `Heat`. | `Hull.Current`, `Shield.Current/LastHitTick`, `Heat.Value` on plasma absorption, `CombatState.LastDamageTick`; initial components of new effects/debris. | Creates `Transform + Motion + Particle` and `Transform + Motion + Debris`. Projectile/debris/particle retirement is deferred through `retired`; `InCombat`, `ShieldBroken`, ship destruction are requested through `states`. |
| `lifecycle` | Current states and entity liveness; `Identity`, `Heat`, `Docked`, `Carrier`, `Brain`, `Transform`, `Motion`, `Collider`, `PhysicsLink`; optional component presence to count removals. Final reads as in `collect`. | Tag values; `Brain.Mode`, supplies inside `Inventory`, position on undocking; `Carrier.Built`; initial components of spawned ships/effects/debris. | Adds, updates or removes five state components; destroys ships and `retired` objects; creates explosion effects, debris and ships. Removes the external physics body on ship destruction; adds it on creation. Commits make changes visible to subsequent traversals. |

New debris is added to the working list immediately. Therefore debris created by projectile hits can participate in the subsequent debris traversal of the same `hits` stage, before the next `physics`. This does not establish stage cost without measurement.

## Logical Component Catalogue

The battle scenario actually attaches **22 distinct types**: **12 base ship types + 2 specializations + 5 optional states + 3 transient-object types**. One further independent type, `Lifetime`, is declared but not attached by current battle logic: **23 declared component types** in total if this reserve type is counted. The two nested inventory types are excluded from these counts.

| Type | Purpose / main data | Where present |
| --- | --- | --- |
| `Identity` | Global serial, team, `ShipClass`. | All ships, including carriers. |
| `Transform` | Current and previous positions, angle. | All five entity families. |
| `Motion` | Velocity, acceleration, angular velocity, speed limit, thrust, damping. | All five families; not every family uses every field. |
| `Hull` | Current/maximum integrity, armor. | All ships. |
| `Shield` | Current/maximum shield, regeneration, last hit and delay. | All ships. |
| `Heat` | Heat, limit and cooling. | All ships. |
| `Brain` | Target, home carrier, mode, next decision tick and orbit sign. | All ships. |
| `Weapon` | Primary weapon: kind, cooldown, next shot, burst, range, damage, speed and shot heat. | All ships. |
| `Collider` | Radius, mass, physical shape count. | All ships; actual fixtures are outside ECS. |
| `PhysicsLink` | Body index in `PhysicsService`. | All ships. |
| `CombatState` | `LastDamageTick`, last damage timestamp. | All ships; a permanent component, not transient `InCombat`. |
| `Inventory` | Capacity, next use tick, inline item array. | All ships. |
| `SecondaryWeapon` | Secondary weapon storing a nested `Weapon` value in its `Value` field. | `Corvette`, `Frigate`, `Destroyer`, `Support` and all carriers. |
| `Carrier` | Construction timer/interval, own escort limit, build count, repair slots. | `LightCarrier`, `FleetCarrier`, `FoundryCarrier`. |
| `Docked` | Carrier reference and minimum undocking tick. | Optional escort state; set after checking for a free repair slot. |
| `Overheated` | Overheat start tick; presence blocks firing and shield regeneration. | Optional ship state. |
| `InCombat` | Combat-state end tick. | Optional ship state after damage. |
| `Boosting` | Boost end tick; increases acceleration and speed limit in `steering`. | Optional ship state. |
| `ShieldBroken` | Shield break tick; removed on recovery above the threshold. | Optional ship state. |
| `Projectile` | Serial, owner/target, weapon kind, damage, radius, lifetime. | Projectile. |
| `Debris` | Serial, lifetime, damage and radius. | Debris. |
| `Particle` | Serial, lifetime, effect kind, size and color. | Particle. |
| `Lifetime` | `EndTick`. | Declared but unused in battle entity compositions. Transient-object lifetimes are stored in their `ExpiresTick`. |

Optional "tags" contain data, so they are not empty markers. Re-enabling an existing state updates its value through `Set`, but does not add a new component. Removing an absent state does not perform a structural removal.

### What Is Not a Separate Scenario Component

| Type / data | Why excluded from logical composition |
| --- | --- |
| `InventoryItem` | Nested `ItemKind + Count` pair in the `Inventory.Items` array. Items are not created as independent entities or attached as separate components. |
| `InventoryItems` | `[InlineArray(16)]`, inline array of `InventoryItem` elements; part of one `Inventory` component. |
| `ShipClass`, `WeaponKind`, `ItemKind`, `BrainMode` | Enums used as component fields. |
| `EntityKey` | `Id + Generation` handle from `EcsContract`, used for references and store access. |
| `ShipRow`, `MovingRow`, requests and `StateKind` | Internal working snapshots, queues and command classification in `Battle`, not components. |
| `RenderEntity`, `BattleFrame`, `CollisionEvent`, `AimSolution`, `RandomStream`, configuration and counters | Presentation, physics messages, calculation result, random generator and supporting data not attached to battle entities. |

[StaticEcsStore.cs](../../SpaceBattle.Adapters/StaticEcsStore.cs) preregisters **25 types** from `Components.cs`: the 23 independent types listed above, plus `InventoryItem` and `InventoryItems`. Registration means a type is allowed by the adapter, not that an entity has a component instance. Registration count therefore cannot be used as the number of components actually involved in battle.

## Typical Entity Compositions

The compositions below are stable after creation/commit completes, not during successive `Set` calls on a new entity.

**Ship base (12):** `Identity`, `Transform`, `Motion`, `Hull`, `Shield`, `Heat`, `Brain`, `Weapon`, `Collider`, `PhysicsLink`, `CombatState`, `Inventory`.

| Family / classes | Permanent composition | Components without states | Possible additional states |
| --- | --- | --- | --- |
| Light escort: `Interceptor`, `Fighter`, `Bomber`, `Gunship` | Ship base. | **12** | `Docked`, `Overheated`, `InCombat`, `Boosting`, `ShieldBroken`. |
| Heavy/service escort: `Corvette`, `Frigate`, `Destroyer`, `Support` | Base + `SecondaryWeapon`. | **13** | The same five. |
| Carrier: `LightCarrier`, `FleetCarrier`, `FoundryCarrier` | Base + `SecondaryWeapon` + `Carrier`. | **14** | `Overheated`, `InCombat`, `Boosting`, `ShieldBroken`; normal AI logic does not dock carriers. |
| Projectile | `Transform`, `Motion`, `Projectile`. | **3** | None in the current implementation. |
| Debris | `Transform`, `Motion`, `Debris`. | **3** | None in the current implementation. |
| Particle | `Transform`, `Motion`, `Particle`. | **3** | None in the current implementation. |

For a particular ship, the component count equals its class base plus the number of states actually present. Do not assume all possible states necessarily coexist. `Docked` does not remove base components, weapons or `PhysicsLink`: the ship remains in ECS, is excluded from the visual frame, skips AI/firing, and its external body is disabled during the physics step.

`Inventory` counts as one component regardless of occupancy: it physically contains 16 inline slots, while `Capacity` sets the number of usable slots (`2 + (int)ShipClass` for escorts, 16 for carriers). `SecondaryWeapon.Value` likewise is not a second independent `Weapon` component instance on that entity.

## ECS, Physics and Rendering Boundaries

- [PhysicsService.cs](../../SpaceBattle.Core/PhysicsService.cs) owns the external Aether.Physics2D world, ship bodies and their fixtures. `Collider` describes shape creation, `PhysicsLink.Body` links ECS to the body. Bodies and fixtures do not increase ECS component counts. Projectiles, debris and particles are integrated in ECS; projectile/debris hits are checked by `Sweep` against ship shapes, not through their own dynamic Aether bodies.
- [BattleInspection.cs](../../SpaceBattle.Core/BattleInspection.cs) (`Capture`) reads ECS and builds `BattleFrame`; [Program.cs](../../SpaceBattle.Game/Program.cs) and [Renderer.cs](../../SpaceBattle.Game/Renderer.cs) render outside `Battle.Step()`. Rendering and external frame capture are excluded from the nine `LastStageMs`. `Capture`, `Checksum` and `Validate` can call `Collect` themselves, but this is not an additional stage inside `Step`.
- [LeoEcsClassicStore.cs](../../SpaceBattle.Adapters/LeoEcsClassicStore.cs), [LeoEcsLiteStore.cs](../../SpaceBattle.Adapters/LeoEcsLiteStore.cs) and [MorpehStore.cs](../../SpaceBattle.Adapters/MorpehStore.cs) add an internal `LifetimeMarker` to each native entity. It is not `SpaceBattle.Lifetime` and is excluded from logical compositions and the counts above. When counting the physical number of native components in these adapters, it is a separate supporting component.
- Morpeh and StaticEcs store logical values through `Wrapper<T>`. The wrapper is not added to a logical component as another composition element: one `Wrapper<T>` represents one logical `T`. [SecsStore.cs](../../SpaceBattle.SECS/SecsStore.cs) uses SECS components and queries directly.

Type count, components per entity, query count and field-access frequency are different quantities. This catalogue establishes the first two from code and describes access paths, but does not rank the "most used" components, compute a live-population mean or draw performance conclusions. Those require separately defined metrics and measurements of a specific run.
