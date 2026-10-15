using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    private static void LifecycleSpatialChecks()
    {
        LifecycleSpatialStoreChecks(static () => new SkecsStore());
        LifecycleSpatialStoreChecks(static () => new LeoEcsLiteStore());
        LifecycleSpatialStoreChecks(static () => new LeoEcsClassicStore());
        LifecycleSpatialStoreChecks(static () => new MorpehStore());
        LifecycleSpatialStoreChecks(static () => new StaticEcsStore());
    }

    private static void LifecycleSpatialStoreChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        LifecycleReparentChecks(factory);
        LifecycleDockContentionChecks(factory, departuresFirst: true);
        LifecycleDockContentionChecks(factory, departuresFirst: false);
    }

    private static Battle<TStore> LifecycleBattle<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        var battle = new Battle<TStore>(factory(), new BattleConfig
        {
            ShipsPerTeam = 8, Seed = 27, ParticleMultiplier = 0, ReplenishCarriers = false
        });
        var frame = new BattleFrame();
        battle.Capture(frame);
        for (var i = 0; i < frame.Ships.Count; i++)
        {
            Require(battle.TryFindShip(frame.Ships[i].Serial, out var e), "lifecycle fixture ship lookup");
            var position = new Vector2(1000 + i * 60, 1000);
            battle.Store.Get<Transform>(e) = new Transform { Position = position, Previous = position };
            LifecycleFreezeShip(battle, e);
        }
        return battle;
    }

    private static void LifecycleFreezeShip<TStore>(Battle<TStore> battle, EntityKey e) where TStore : IEcsStore
    {
        battle.Store.Get<Motion>(e) = default;
        battle.Store.Get<Brain>(e) = new Brain
        {
            Mode = BrainMode.Patrol, NextDecisionTick = int.MaxValue, Home = EntityKey.None,
            Target = EntityKey.None, SquadLeader = EntityKey.None, CoverAlly = EntityKey.None
        };
        battle.Store.Get<Weapon>(e).NextFireTick = int.MaxValue;
        if (battle.Store.Has<SecondaryWeapon>(e))
            battle.Store.Get<SecondaryWeapon>(e).Value.NextFireTick = int.MaxValue;
        if (battle.Store.Has<Carrier>(e)) battle.Store.Get<Carrier>(e).NextBuildTick = int.MaxValue;
    }

    private static EntityKey LifecycleActor<TStore>(Battle<TStore> battle, ShipClass kind, Vector2 position)
        where TStore : IEcsStore
    {
        var e = battle.SpawnShip(0, kind, position, EntityKey.None);
        LifecycleFreezeShip(battle, e);
        return e;
    }

    private static List<EntityKey> LifecycleMounts<TStore>(Battle<TStore> battle, EntityKey? parent = null)
        where TStore : IEcsStore
    {
        var collector = new LifecycleMountCollector { Parent = parent, Entities = new() };
        battle.Store.ForEach<MountedTurret, LifecycleMountCollector>(ref collector);
        collector.Entities.Sort((a, b) => battle.Store.Get<MountedTurret>(a).Serial.CompareTo(
            battle.Store.Get<MountedTurret>(b).Serial));
        return collector.Entities;
    }

    private struct LifecycleMountCollector : IProcessor<MountedTurret>
    {
        public EntityKey? Parent;
        public List<EntityKey> Entities;
        public void Execute(EntityKey e, ref MountedTurret mount)
        {
            if (!Parent.HasValue || mount.Parent == Parent.Value) Entities.Add(e);
        }
    }

    private static void LifecycleFreezeMounts<TStore>(Battle<TStore> battle) where TStore : IEcsStore
    {
        foreach (var gun in LifecycleMounts(battle))
            battle.Store.Get<Weapon>(gun).NextFireTick = int.MaxValue;
    }

    private static BattleFrame LifecycleFrameParity<TStore>(Battle<TStore> battle, string scenario)
        where TStore : IEcsStore
    {
        // Read the finalized cache before either the default capture or Validate can refresh it.
        var cached = new BattleFrame(); var queried = new BattleFrame();
        battle.Capture(cached, refreshEntities: false);
        battle.Capture(queried);
        Require(cached.Ships.SequenceEqual(queried.Ships) && cached.Turrets.SequenceEqual(queried.Turrets) &&
            cached.Projectiles.SequenceEqual(queried.Projectiles) && cached.Debris.SequenceEqual(queried.Debris) &&
            cached.Particles.SequenceEqual(queried.Particles), $"{battle.Framework}: {scenario} cached/default frame parity");
        battle.Validate();
        return cached;
    }

    private static void LifecycleReparentChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var battle = LifecycleBattle(factory);
        var first = LifecycleActor(battle, ShipClass.Frigate, new(-50, 0));
        var second = LifecycleActor(battle, ShipClass.Frigate, new(50, 0));
        battle.Store.Commit();
        LifecycleFreezeMounts(battle);
        var firstGuns = LifecycleMounts(battle, first);
        var secondGuns = LifecycleMounts(battle, second);
        var count = ShipCatalog.TurretCount(ShipClass.Frigate);
        Require(count > 0 && firstGuns.Count == count && secondGuns.Count == count,
            $"{battle.Framework}: same-class reparent fixture mount counts");
        var firstSerial = battle.Store.Get<Identity>(first).Serial;
        var secondSerial = battle.Store.Get<Identity>(second).Serial;
        var survivingSerials = firstGuns.Select(g => battle.Store.Get<MountedTurret>(g).Serial).ToArray();
        foreach (var gun in firstGuns) battle.Store.Get<MountedTurret>(gun).Parent = second;
        foreach (var gun in secondGuns) battle.Store.Get<MountedTurret>(gun).Parent = first;
        battle.Store.Commit();
        battle.Validate();
        var turrets = battle.TurretCount; var bodies = battle.PhysicsBodies; var fixtures = battle.PhysicsFixtures;
        var deaths = battle.Counters.ShipsDestroyed;

        battle.Damage(first, 1000000, false, false);
        battle.Step();
        Require(!battle.Store.Alive(first) && battle.Store.Alive(second) && secondGuns.All(g => !battle.Store.Alive(g)) &&
            firstGuns.All(g => battle.Store.Alive(g) && battle.Store.Get<MountedTurret>(g).Parent == second),
            $"{battle.Framework}: destruction uses current mount parents, not spawn-time ownership");
        Require(LifecycleMounts(battle, first).Count == 0 &&
            LifecycleMounts(battle, second).SequenceEqual(firstGuns), $"{battle.Framework}: no orphan or missing swapped mounts");
        Require(battle.TurretCount == turrets - count && battle.PhysicsBodies == bodies - 1 &&
            battle.PhysicsFixtures == fixtures - ShipCatalog.Shapes(ShipClass.Frigate) &&
            battle.Counters.ShipsDestroyed == deaths + 1, $"{battle.Framework}: swapped parent lifetime counts");
        var frame = LifecycleFrameParity(battle, "swapped parent destruction");
        Require(frame.Ships.All(s => s.Serial != firstSerial) && frame.Turrets.All(g => g.ParentSerial != firstSerial) &&
            frame.Turrets.Where(g => g.ParentSerial == secondSerial).Select(g => g.Serial).SequenceEqual(survivingSerials),
            $"{battle.Framework}: cached frame preserves only the current surviving parent's guns");

        battle.Damage(second, 1000000, false, false);
        battle.Step();
        Require(firstGuns.All(g => !battle.Store.Alive(g)) && battle.TurretCount == turrets - count * 2,
            $"{battle.Framework}: subsequent destruction removes the remaining swapped mounts");
        LifecycleFrameParity(battle, "second swapped parent destruction");
        Console.WriteLine($"PASS: {battle.Framework} swapped mount ownership, destruction, cached/default capture and Validate.");
    }

    private static void LifecycleDockContentionChecks<TStore>(Func<TStore> factory, bool departuresFirst)
        where TStore : IEcsStore
    {
        using var battle = LifecycleBattle(factory);
        var home = LifecycleActor(battle, ShipClass.LightCarrier, Vector2.Zero);
        battle.Store.Get<Carrier>(home).RepairSlots = 2;
        var arrivals = new EntityKey[3];
        EntityKey departing, doomed;
        if (departuresFirst)
        {
            departing = LifecycleActor(battle, ShipClass.Fighter, Vector2.Zero);
            doomed = LifecycleActor(battle, ShipClass.Fighter, Vector2.Zero);
            SpawnArrivals();
        }
        else
        {
            SpawnArrivals();
            departing = LifecycleActor(battle, ShipClass.Fighter, Vector2.Zero);
            doomed = LifecycleActor(battle, ShipClass.Fighter, Vector2.Zero);
        }
        battle.Store.Set(departing, new Docked { Carrier = home, ReleaseTick = 0 });
        battle.Store.Set(doomed, new Docked { Carrier = home, ReleaseTick = int.MaxValue });
        battle.Store.Commit();
        LifecycleFreezeMounts(battle);
        var dockings = battle.Counters.Dockings;
        var deaths = battle.Counters.ShipsDestroyed;
        battle.Damage(doomed, 1000000, false, false);
        battle.Step();
        Require(battle.Store.Alive(departing) && !battle.Store.Has<Docked>(departing) && !battle.Store.Alive(doomed) &&
            battle.Counters.ShipsDestroyed == deaths + 1, $"{battle.Framework}: same-tick undock and docked ship destruction");
        if (departuresFirst)
            CheckWinners();
        else
        {
            Require(arrivals.All(e => !battle.Store.Has<Docked>(e)) && battle.Counters.Dockings == dockings,
                $"{battle.Framework}: earlier arrivals cannot consume slots released by later serials");
            LifecycleFrameParity(battle, "arrivals before dock releases");
            foreach (var e in arrivals) battle.Store.Get<Brain>(e).NextDecisionTick = 0;
            battle.Step();
            CheckWinners();
        }
        var frame = LifecycleFrameParity(battle, "dock contention");
        var dockedSerials = arrivals.Take(2).Select(e => battle.Store.Get<Identity>(e).Serial).ToArray();
        Require(frame.Ships.All(s => !dockedSerials.Contains(s.Serial)) &&
            frame.Ships.Any(s => s.Serial == battle.Store.Get<Identity>(arrivals[2]).Serial),
            $"{battle.Framework}: cached/default capture hides dock winners but retains rejected arrival");
        Console.WriteLine($"PASS: {battle.Framework} dock contention, departuresFirst={departuresFirst}, serial-ordered releases and capacity.");

        void SpawnArrivals()
        {
            var distance = ShipCatalog.Radius(ShipClass.LightCarrier) + ShipCatalog.Radius(ShipClass.Fighter) + 1;
            for (var i = 0; i < arrivals.Length; i++)
            {
                var angle = i * MathF.Tau / arrivals.Length;
                var e = LifecycleActor(battle, ShipClass.Fighter, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance);
                arrivals[i] = e;
                battle.Store.Get<Hull>(e).Current = battle.Store.Get<Hull>(e).Maximum * 0.2f;
                ref var brain = ref battle.Store.Get<Brain>(e);
                brain.Home = home; brain.Mode = BrainMode.Retreat; brain.NextDecisionTick = 0;
            }
        }
        void CheckWinners()
        {
            Require(arrivals.Take(2).All(e => battle.Store.Has<Docked>(e) && battle.Store.Get<Docked>(e).Carrier == home) &&
                !battle.Store.Has<Docked>(arrivals[2]) && battle.Counters.Dockings == dockings + 2,
                $"{battle.Framework}: two freed slots admit the first two arrivals exactly once");
        }
    }
}
