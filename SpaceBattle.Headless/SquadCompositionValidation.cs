using System.Numerics;
using System.Reflection;
using SpaceBattle;

internal static class SquadCompositionValidation
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        BoundedIndex();
        Run(static () => new SkecsStore()); Run(static () => new LeoEcsLiteStore());
        Run(static () => new LeoEcsClassicStore()); Run(static () => new MorpehStore());
        Run(static () => new StaticEcsStore());
        Console.WriteLine("PASS: useful mixed/homogeneous squads, compatibility, phase leases, death repair, screen/recovery, lane geometry, dense budgets and warmed phase allocations on five adapters.");
    }
    private static void Run<T>(Func<T> factory) where T : IEcsStore
    {
        Composition(factory, 120, 120, WeaponKind.Cannon, WeaponKind.Plasma);
        Composition(factory, 400, 15, WeaponKind.Plasma, WeaponKind.Plasma);
        Composition(factory, 0, 400, WeaponKind.Cannon, WeaponKind.Cannon);
        Compatibility(factory); DistantProbes(factory); LeasesAndDeaths(factory); ExternalRoster(factory); ScreenAndRecovery(factory); DenseProgress(factory); DenseAndAllocations(factory);
    }
    private static void Composition<T>(Func<T> factory, float shield, float hull, WeaponKind leaderKind, WeaponKind preferred) where T : IEcsStore
    {
        using var f = new Fixture<T>(factory());
        var leader = f.Spawn(0, Vector2.Zero, leaderKind);
        var alternatives = new List<EntityKey>();
        for (var i = 0; i < 3; i++) alternatives.Add(f.Spawn(0, new(0, 5 + i * 3), preferred));
        var other = f.Spawn(0, new(0, 4), preferred == WeaponKind.Plasma ? WeaponKind.Cannon : WeaponKind.Plasma);
        var target = f.Spawn(1, new(50, 0), WeaponKind.Missile, false);
        f.Health(target, shield, hull); f.Refresh();
        Check(f.Store.Get<Brain>(leader).SquadSize == 4, "useful group fills four slots");
        var squad = f.Squad(leader);
        var first = f.Rows[squad.Members[1]].Entity;
        Check(f.Store.Get<Weapon>(first).Kind == preferred, "utility beats closer wrong specialization");
        if (leaderKind == preferred)
        {
            Check(alternatives.All(e => f.Store.Get<Brain>(e).SquadLeader == leader), "homogeneous specialization preferred");
            Check(f.Store.Get<Brain>(other).SquadLeader != leader, "diversity is not mandatory");
        }
        Check(squad.Count is >= 2 and <= 4 && squad.Members[0] == f.Index(leader), "leader and bounded size");
    }
    private static void Compatibility<T>(Func<T> factory) where T : IEcsStore
    {
        using var f = new Fixture<T>(factory());
        var leader = f.Spawn(0, Vector2.Zero, WeaponKind.Cannon);
        var slow = f.Spawn(0, new(0, 2), WeaponKind.Plasma); f.Store.Get<Motion>(slow).MaxSpeed = 3;
        var shortRange = f.Spawn(0, new(0, 4), WeaponKind.Plasma); f.Store.Get<Weapon>(shortRange).Range = 12;
        var delayed = f.Spawn(0, new(0, 6), WeaponKind.Plasma); f.Store.Get<Weapon>(delayed).NextFireTick = 1200;
        var big = f.Spawn(0, new(0, 8), WeaponKind.Plasma, true, ShipClass.Corvette);
        var partner = f.Spawn(0, new(0, 10), WeaponKind.Plasma);
        f.Spawn(1, new(50, 0), WeaponKind.Cannon, false);
        f.Refresh();
        Check(f.Store.Get<Brain>(partner).SquadLeader == leader, "compatible partner recruited");
        foreach (var e in new[] { slow, shortRange, delayed, big })
            Check(f.Store.Get<Brain>(e).SquadLeader != leader, "incompatible speed/range/readiness/class excluded");
    }
    private static void DistantProbes<T>(Func<T> factory) where T : IEcsStore
    {
        using var f = new Fixture<T>(factory());
        for (var i = 0; i < 20; i++) f.Spawn(1, new(1000 + i * 20, 1000), WeaponKind.Cannon, false);
        var leader = f.Spawn(0, Vector2.Zero, WeaponKind.Cannon);
        var ally = f.Spawn(0, new(0, 6), WeaponKind.Plasma);
        var nearby = f.Spawn(1, new(50, 0), WeaponKind.Cannon, false);
        f.Refresh();
        Check(f.Store.Get<Brain>(ally).SquadLeader == leader && f.Store.Get<Brain>(ally).Target == nearby,
            "unreachable lower-serial probes cannot crowd accessible formation targets");
    }
    private static void LeasesAndDeaths<T>(Func<T> factory) where T : IEcsStore
    {
        using var f = new Fixture<T>(factory());
        var ships = new[] { f.Spawn(0, Vector2.Zero, WeaponKind.Plasma), f.Spawn(0, new(0, 6), WeaponKind.Cannon),
            f.Spawn(0, new(0, 12), WeaponKind.Cannon), f.Spawn(0, new(0, 18), WeaponKind.Plasma) };
        var target = f.Spawn(1, new(50, 0), WeaponKind.Cannon, false); f.Health(target, 400, 1000);
        f.Refresh(); var leader = f.Store.Get<Brain>(ships[0]).SquadLeader;
        Check(!leader.IsNone && ships.All(e => f.Store.Get<Brain>(e).Target == target), "shared target");
        for (var i = 0; i < 12; i++)
        {
            f.Store.Get<Shield>(target).Current = i < 6 ? 400 - i * 60 : 0;
            f.SetTick(i + 1); f.Refresh();
            Check(ships.All(e => f.Store.Get<Brain>(e).SquadLeader == leader && f.Store.Get<Brain>(e).Target == target), "shield/hull phase does not churn roster/lease");
        }
        var lateral = f.Steering(ships[1]);
        Check(float.IsFinite(lateral.X + lateral.Y) && lateral.LengthSquared() > 0, "individual target-relative lane steering");
        f.Kill(leader); f.Refresh();
        var survivors = ships.Where(e => f.Store.Alive(e)).ToArray();
        var elected = f.Store.Get<Brain>(survivors[0]).SquadLeader;
        Check(!elected.IsNone && elected != leader && survivors.All(e => f.Store.Get<Brain>(e).SquadLeader == elected), "leader death retains surviving roster and elects survivor");
        f.Kill(survivors[^1]); f.Refresh();
        Check(f.Store.Get<Brain>(survivors[0]).SquadSize == 2, "member death repairs group count");
        var next = f.Spawn(1, new(52, 0), WeaponKind.Cannon, false);
        f.Kill(target); f.Refresh();
        Check(f.Store.Get<Brain>(survivors[0]).Target == next, "target death releases lease immediately");
    }
    private static void ScreenAndRecovery<T>(Func<T> factory) where T : IEcsStore
    {
        using var f = new Fixture<T>(factory());
        var wounded = f.Spawn(0, new(-4, 0), WeaponKind.Cannon);
        var healthy = f.Spawn(0, Vector2.Zero, WeaponKind.Cannon);
        var target = f.Spawn(1, new(-45, 0), WeaponKind.Cannon, false); f.Health(target, 1000, 10000);
        f.Refresh(); f.Store.Get<Hull>(wounded).Current = 45; f.Store.Get<Shield>(wounded).Current = 3;
        f.Store.Get<CombatState>(wounded).LastAttacker = target; f.Store.Get<CombatState>(wounded).LastDamageTick = f.Battle.Tick;
        f.Store.Get<Motion>(wounded).Thrust = 20; f.Store.Get<Brain>(wounded).NextDecisionTick = 0;
        f.Battle.Step();
        Check(f.Store.Get<Brain>(wounded).Mode == BrainMode.Screen && f.Store.Get<Brain>(wounded).CoverAlly == healthy, "wounded member takes healthier ally cover");
        f.Health(wounded, 50, 100); f.Store.Get<Brain>(wounded).NextDecisionTick = 0; f.Battle.Step();
        Check(f.Store.Get<Brain>(wounded).Mode == BrainMode.Attack && f.Store.Get<Brain>(wounded).CoverAlly.IsNone, "recovered member returns to attack");
        var leader = f.Store.Get<Brain>(healthy).SquadLeader;
        f.Store.Set(wounded, new Overheated()); f.Store.Get<Heat>(wounded).Value = 105; f.Refresh();
        Check(f.Store.Get<Brain>(healthy).SquadLeader == leader, "temporary heat does not churn useful group");
    }
    private static void ExternalRoster<T>(Func<T> factory) where T : IEcsStore
    {
        using var f = new Fixture<T>(factory());
        var ships = Enumerable.Range(0, 4).Select(i => f.Spawn(0, new(0, i * 6), WeaponKind.Cannon)).ToArray();
        f.Spawn(1, new(50, 0), WeaponKind.Cannon, false);
        for (var i = 0; i < 4; i++) { f.Store.Get<Brain>(ships[i]).SquadLeader = ships[i < 2 ? 0 : 2]; f.Store.Get<Brain>(ships[i]).SquadSize = 2; }
        f.Store.Get<Brain>(ships[0]).SquadLeader = ships[2];
        f.Refresh();
        var buffers = (Squad[])typeof(Battle<T>).GetField("squadBuffers", Hidden)!.GetValue(f.Battle)!;
        var count = (int)typeof(Battle<T>).GetField("squadCount", Hidden)!.GetValue(f.Battle)!;
        var seen = new HashSet<int>();
        foreach (var squad in buffers.Take(count).Where(s => s.Count >= 2))
            for (var s = 0; s < squad.Count; s++) Check(seen.Add(squad.Members[s]), "external reparent cannot insert leader into two groups");
        f.Store.Get<Identity>(ships[1]).Team = 1; f.Refresh();
        var leader = f.Store.Get<Brain>(ships[1]).SquadLeader;
        Check(leader.IsNone || f.Store.Get<Identity>(leader).Team == 1, "external team edit cannot retain hostile group leader");
    }
    private static void DenseAndAllocations<T>(Func<T> factory) where T : IEcsStore
    {
        using var f = new Fixture<T>(factory(), new TacticsSettings { MaximumSpatialWorkPerTick = 32,
            MaximumScoreEvaluationsPerTick = 16, MaximumFormationEvaluationsPerReview = 8 });
        var own = Enumerable.Range(0, 64).Select(i => f.Spawn(0, new(i % 8, i / 8), (i & 1) == 0 ? WeaponKind.Cannon : WeaponKind.Plasma)).ToArray();
        f.Spawn(1, new(60, 0), WeaponKind.Cannon, false);
        for (var i = 0; i < 4; i++) { f.Store.Get<Brain>(own[i]).SquadLeader = own[0]; f.Store.Get<Brain>(own[i]).SquadSize = 4; }
        f.Collect();
        for (var i = 0; i < 8; i++) f.Prepare();
        var spatial = f.Battle.Tactics.SpatialWork; var scores = f.Battle.Tactics.ScoreEvaluations;
        f.Prepare();
        Check(f.Battle.Tactics.SpatialWork - spatial <= 32 && f.Battle.Tactics.ScoreEvaluations - scores <= 16 &&
            f.Battle.Tactics.BudgetFallbacks > 0, "dense work respects budgets with explicit fallback");
        Check(own.Take(4).All(e => f.Store.Get<Brain>(e).SquadLeader == own[0]), "budget exhaustion retains existing group");
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) f.Prepare();
        Check(GC.GetAllocatedBytesForCurrentThread() == before, typeof(T).Name + " reserved tactical phase has no new allocations");
    }
    private static void DenseProgress<T>(Func<T> factory) where T : IEcsStore
    {
        using var f = new Fixture<T>(factory());
        var ships = Enumerable.Range(0, 32).Select(i => f.Spawn(0, new(i % 8, i / 8), WeaponKind.Cannon)).ToArray();
        f.Spawn(1, new(60, 0), WeaponKind.Cannon, false); f.Refresh();
        Check(ships.All(e => !f.Store.Get<Brain>(e).SquadLeader.IsNone), "assigned prefix cannot starve later dense eligible ships");
    }
    private static void BoundedIndex()
    {
        var grid = new SpatialPointIndex(); grid.Reserve(1024); var output = new List<int>(32);
        for (var i = 0; i < 1024; i++) grid.Add(new(1, 1), i);
        grid.Seal(); var work = 64; grid.QueryLimited(Vector2.Zero, 48, output, 16, ref work);
        var expected = output.ToArray(); Check(output.Count == 16 && work >= 0 && output.SequenceEqual(output.Order()), "bounded dense sample sorted");
        work = 64; grid.QueryLimited(Vector2.Zero, 48, output, 16, ref work);
        Check(output.SequenceEqual(expected), "bounded sample deterministic");
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) { work = 64; grid.QueryLimited(Vector2.Zero, float.MaxValue, output, 16, ref work); }
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "bounded huge query zero allocation");
        work = 0; grid.QueryLimited(Vector2.Zero, 48, output, 16, ref work); Check(output.Count == 0, "empty exhausted query");
    }
    private sealed class Fixture<T> : IDisposable where T : IEcsStore
    {
        public readonly Battle<T> Battle; public T Store => Battle.Store;
        public readonly Action Prepare, Collect;
        private readonly Action flush;
        public List<AiShipSnapshot> Rows => (List<AiShipSnapshot>)typeof(Battle<T>).GetField("aiRows", Hidden)!.GetValue(Battle)!;
        public Fixture(T store, TacticsSettings? settings = null)
        {
            Battle = new(store, new BattleConfig { ShipsPerTeam = 8, Seed = 61012, Tactics = settings ?? new() });
            Prepare = typeof(Battle<T>).GetMethod("PrepareTactics", Hidden)!.CreateDelegate<Action>(Battle);
            Collect = typeof(Battle<T>).GetMethod("Collect", Hidden)!.CreateDelegate<Action>(Battle);
            flush = typeof(Battle<T>).GetMethod("FlushLifecycle", Hidden)!.CreateDelegate<Action>(Battle);
            var frame = new BattleFrame(); Battle.Capture(frame);
            foreach (var row in frame.Ships) { Battle.TryFindShip(row.Serial, out var e); Store.Set(e, new Docked { Carrier = EntityKey.None }); Store.Get<Brain>(e).NextDecisionTick = int.MaxValue; }
        }
        public EntityKey Spawn(int team, Vector2 position, WeaponKind kind, bool active = true, ShipClass shipClass = ShipClass.Fighter)
        {
            var e = Battle.SpawnShip(team, shipClass, position, EntityKey.None);
            Store.Get<Brain>(e) = new Brain { Mode = active ? BrainMode.Attack : BrainMode.Patrol, NextDecisionTick = active ? 0 : int.MaxValue,
                Target = EntityKey.None, Home = EntityKey.None, SquadLeader = EntityKey.None, CoverAlly = EntityKey.None, OrbitSign = 1 };
            Store.Get<Weapon>(e) = new Weapon { Kind = kind, Damage = 6, Burst = 1, Cooldown = 60, Speed = 100, Range = 100 };
            Store.Get<Motion>(e) = new Motion { MaxSpeed = 40 };
            Store.Get<Heat>(e) = new Heat { Limit = 100, CoolingPerSecond = 100 };
            Store.Get<Inventory>(e).NextUseTick = int.MaxValue;
            if (Store.Has<SecondaryWeapon>(e)) Store.Get<SecondaryWeapon>(e).Value = default;
            Health(e, 50, 100); Store.Commit(); return e;
        }
        public void Health(EntityKey e, float shield, float hull) { Store.Get<Shield>(e) = new Shield { Current = shield, Maximum = MathF.Max(1, shield) }; Store.Get<Hull>(e) = new Hull { Current = hull, Maximum = hull }; }
        public void Refresh() { Store.Commit(); Collect(); Prepare(); }
        public int Index(EntityKey e) => Rows.FindIndex(row => row.Entity == e);
        public Squad Squad(EntityKey e)
        {
            var indexes = (EntityIndexTable)typeof(Battle<T>).GetField("squadIndexes", Hidden)!.GetValue(Battle)!;
            var buffers = (Squad[])typeof(Battle<T>).GetField("squadBuffers", Hidden)!.GetValue(Battle)!;
            return buffers[indexes[Store.Get<Brain>(e).SquadLeader]];
        }
        public void SetTick(int value) => typeof(Battle<T>).GetField("<Tick>k__BackingField", Hidden)!.SetValue(Battle, value);
        public void Kill(EntityKey e) { Battle.Damage(e, 100000, false, false); flush(); }
        public Vector2 Steering(EntityKey e) => (Vector2)typeof(Battle<T>).GetMethod("FormationSteering", Hidden)!.Invoke(Battle,
            [e, Store.Get<Brain>(e), Store.Get<Transform>(e).Position, Store.Get<Motion>(e).MaxSpeed])!;
        public void Dispose() => Battle.Dispose();
    }
    private static void Check(bool test, string label) { if (!test) throw new InvalidOperationException(label); }
}
