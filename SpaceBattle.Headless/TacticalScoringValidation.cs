using System.Numerics;
using System.Reflection;
using SpaceBattle;

internal static class TacticalScoringValidation
{
    public static void Run() => AiLocalityValidation.RunScoring();
}

internal static partial class AiLocalityValidation
{
    public static void RunScoring()
    {
        RunScoring(static () => new SkecsStore()); RunScoring(static () => new LeoEcsLiteStore());
        RunScoring(static () => new LeoEcsClassicStore()); RunScoring(static () => new MorpehStore());
        RunScoring(static () => new StaticEcsStore());
        Console.WriteLine("PASS: tactical v3 readiness/pursuit/staggering, heat-limited volleys, intent confidence, overkill, separate suppression/losses, bounded candidates and allocation checks (five adapters).");
    }
    public static void RunScoring<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        ReadyAndPursuitChecks(factory); StaggerChecks(factory); FirstHeatChecks(factory);
        PendingChecks(factory); OverkillChecks(factory); SuppressionChecks(factory);
        BoundedSelectionChecks(factory); CanonicalImpactChecks(factory); ScoringAllocationChecks(factory);
    }
    private static void EstimateParity<TStore>(Fixture<TStore> f, int actor, Squad? squad, int target) where TStore : IEcsStore
    {
        var actual = f.Estimate(actor, squad, target); var expected = f.Oracle().Estimate(actor, squad, target);
        Parity(actual.KillSeconds, expected.KillSeconds, "independent arrival/layer kill time");
        Parity(actual.Suppression, expected.Suppression, "independent suppression");
        Parity(actual.ExpectedLosses, expected.ExpectedLosses, "independent losses");
        Parity(actual.Cost, expected.Cost, "independent composition cost");
        Bits(f.Cost(actor, squad, target), actual.Cost, "TargetCost delegates to composition estimate");
    }
    private static void ReadyAndPursuitChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var f = new Fixture<TStore>(factory(), LayerConfig());
        var actor = f.Spawn(0, ShipClass.Fighter, Vector2.Zero); var target = f.Spawn(1, ShipClass.Fighter, Vector2.Zero);
        f.Battle.Store.Get<Weapon>(actor) = LayerWeapon(WeaponKind.Cannon);
        LayerHealth(f, target, 0, 60); f.Prepare(); var a = f.Index(actor); var t = f.Index(target);
        var ready = f.Estimate(a, null, t);
        f.Battle.Store.Get<Weapon>(actor).NextFireTick = 120; f.Prepare();
        Parity(f.Estimate(a, null, t).KillSeconds, ready.KillSeconds + 2, "first volley waits real NextFireTick");
        Check(f.Engage(a, t) >= 2 && f.Range(a, t) > 0, "formation helpers reflect readiness");
        f.Battle.Store.Get<Weapon>(actor).NextFireTick = int.MaxValue; f.Prepare();
        Check(float.IsPositiveInfinity(f.Cost(a, null, t)) && float.IsPositiveInfinity(f.Engage(a, t)) && f.Range(a, t) == 0,
            "int.MaxValue weapon is unavailable, not a free ready volley");
        f.Battle.Store.Get<Weapon>(actor) = LayerWeapon(WeaponKind.Cannon); f.Battle.Store.Get<Weapon>(actor).Speed = 500;
        f.Battle.Store.Get<Weapon>(actor).NextFireTick = 120;
        f.Battle.Store.Get<Transform>(target).Position = new(50, 0);
        f.Battle.Store.Get<Motion>(target).Velocity = new(80, 0); f.Battle.Store.Get<Motion>(actor).MaxSpeed = 20;
        f.Prepare();
        Check(float.IsPositiveInfinity(f.Cost(a, null, t)), "cooldown projects receding target beyond range; no impossible future firing");
        f.Battle.Store.Get<Motion>(actor).MaxSpeed = 200; f.Prepare();
        Check(float.IsFinite(f.Cost(a, null, t)) && f.Engage(a, t) > 3, "constant-velocity pursuit adds extra closing time");
        EstimateParity(f, a, null, t);
    }
    private static void StaggerChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var f = new Fixture<TStore>(factory(), LayerConfig());
        var leader = f.Spawn(0, ShipClass.Fighter, Vector2.Zero); var wing = f.Spawn(0, ShipClass.Fighter, Vector2.Zero);
        var target = f.Spawn(1, ShipClass.Fighter, Vector2.Zero);
        foreach (var e in new[] { leader, wing })
        { f.Battle.Store.Get<Weapon>(e) = LayerWeapon(WeaponKind.Cannon); f.Battle.Store.Get<Brain>(e).Mode = BrainMode.Attack; }
        LayerHealth(f, target, 120, 120); f.Prepare(); var a = f.Index(leader); var b = f.Index(wing); var t = f.Index(target);
        var squad = new Squad { Count = 2, Leader = a }; squad.Members[0] = a; squad.Members[1] = b;
        var simultaneous = f.Estimate(a, squad, t).KillSeconds;
        f.Battle.Store.Get<Transform>(wing).Position = new(200, 0); f.Battle.Store.Get<Motion>(wing).MaxSpeed = 100;
        f.Prepare();
        Check(f.Estimate(a, squad, t).KillSeconds > simultaneous, "off-range member supplies neither initial volley nor full DPS before arrival");
        EstimateParity(f, a, squad, t);
        f.Battle.Store.Get<Motion>(wing).MaxSpeed = 15; f.Prepare();
        Parity(f.Estimate(a, squad, t).KillSeconds, f.Estimate(a, null, t).KillSeconds, "slow out-of-horizon member excluded");
        f.Battle.Store.Get<Transform>(wing).Position = Vector2.Zero;
        f.Battle.Store.Get<Weapon>(wing).NextFireTick = 240; f.Prepare();
        Parity(f.Cost(a, squad, t), f.Cost(a, null, t), "late ready participant beyond join delay excluded");
        f.Battle.Store.Get<Weapon>(wing).NextFireTick = 60; f.Prepare();
        Check(f.Estimate(a, squad, t).KillSeconds < f.Estimate(a, null, t).KillSeconds, "timely staggered contributor improves kill time");
        EstimateParity(f, a, squad, t);
    }
    private static void FirstHeatChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var f = new Fixture<TStore>(factory(), LayerConfig());
        var actor = f.Spawn(0, ShipClass.Fighter, Vector2.Zero); var target = f.Spawn(1, ShipClass.Fighter, Vector2.Zero);
        var weapon = LayerWeapon(WeaponKind.Cannon, 2, 1); weapon.Burst = 10;
        f.Battle.Store.Get<Weapon>(actor) = weapon; f.Battle.Store.Get<Heat>(actor) = new Heat { Limit = 1 };
        LayerHealth(f, target, 0, 5); f.Prepare(); var a = f.Index(actor); var t = f.Index(target);
        Check(float.IsPositiveInfinity(f.Cost(a, null, t)), "only one first-burst shot fits headroom; no heat for repeat volleys");
        LayerHealth(f, target, 0, 3); f.Prepare();
        Parity(f.Estimate(a, null, t).KillSeconds, 1f / 60, "initial allowed shot retained even with zero sustainable cadence");
        LayerHealth(f, target, 0, 30); f.Battle.Store.Get<Heat>(actor).CoolingPerSecond = 1; f.Prepare();
        Check(float.IsFinite(f.Cost(a, null, t)), "cooling restores sustainable fire without double-spending first headroom");
        EstimateParity(f, a, null, t);
        f.Battle.Store.Get<Heat>(actor) = new Heat { Value = 100, Limit = 100, CoolingPerSecond = 20 };
        f.Battle.Store.Set(actor, new Overheated()); f.Prepare();
        Check(f.Engage(a, t) > 2.7f, "overheated snapshot waits recovery below 45 percent");
        EstimateParity(f, a, null, t);
        f.Battle.Store.Get<Heat>(actor).CoolingPerSecond = 0; f.Prepare();
        Check(float.IsPositiveInfinity(f.Cost(a, null, t)), "overheated ship with no cooling cannot participate");
    }
    private static void PendingChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        float low = 0;
        foreach (var confidence in new[] { 0.1f, 0.8f })
        {
            var config = LayerConfig() with { Tactics = new TacticsSettings { IncomingHitConfidence = confidence } };
            using var f = new Fixture<TStore>(factory(), config);
            var actor = f.Spawn(0, ShipClass.Fighter, Vector2.Zero); var target = f.Spawn(1, ShipClass.Fighter, new(20, 0));
            f.Battle.Store.Get<Weapon>(actor) = LayerWeapon(WeaponKind.Cannon); LayerHealth(f, target, 40, 100, 0.5f);
            f.Prepare(); var a = f.Index(actor); var t = f.Index(target); var original = f.Estimate(a, null, t).KillSeconds;
            var shot = f.Battle.Store.Create();
            f.Battle.Store.Set(shot, new Projectile { Serial = 100000, Owner = actor, Target = target, Kind = WeaponKind.Cannon,
                Damage = 100, Radius = 0.2f, ExpiresTick = 1000 });
            f.Battle.Store.Set(shot, new Transform { Position = Vector2.Zero });
            f.Battle.Store.Set(shot, new Motion { Velocity = new(100, 0) });
            f.Prepare();
            var shield = f.Field<float[]>("committedShield")[t]; var hull = f.Field<float[]>("committedHull")[t];
            Parity(shield, MathF.Min(20, 40 * confidence), "intent shield confidence and half-HP cap");
            Parity(hull, MathF.Min(50, 60 * config.Damage.Kinetic.HullMultiplier * 0.5f * confidence), "intent hull is already armored");
            var estimate = f.Estimate(a, null, t);
            Check(estimate.KillSeconds < original && estimate.KillSeconds > f.Engage(a, t), "pending expectation helps but does not guarantee destruction");
            EstimateParity(f, a, null, t);
            if (confidence == 0.1f) low = estimate.KillSeconds;
            else Check(estimate.KillSeconds < low, "greater incoming confidence changes forecast once, not twice");
            f.Field<float[]>("committedShield")[t] = 100000; f.Field<float[]>("committedHull")[t] = 100000;
            var capped = f.Estimate(a, null, t).KillSeconds;
            f.Field<float[]>("committedShield")[t] = 20; f.Field<float[]>("committedHull")[t] = 50;
            Bits(capped, f.Estimate(a, null, t).KillSeconds, "scorer defensively caps oversized pending layers");
        }
    }
    private static void OverkillChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var f = new Fixture<TStore>(factory(), LayerConfig());
        var actor = f.Spawn(0, ShipClass.Fighter, Vector2.Zero); var target = f.Spawn(1, ShipClass.Fighter, new(40, 0));
        f.Battle.Store.Get<Weapon>(actor) = LayerWeapon(WeaponKind.Cannon);
        LayerHealth(f, target, 0, 3); f.Battle.Store.Get<Collider>(target).Radius = 0.1f; f.Prepare(); var a = f.Index(actor); var t = f.Index(target);
        var small = f.Estimate(a, null, t).KillSeconds;
        f.Battle.Store.Get<Weapon>(actor).Damage = 600; f.Prepare();
        Parity(f.Estimate(a, null, t).KillSeconds, small, "one-shot overkill does not manufacture hundredfold useful DPS");
        f.Battle.Store.Get<Motion>(target).Velocity = new(0, 40); f.Prepare();
        Check(f.Estimate(a, null, t).KillSeconds > small, "small moving target lowers hit probability");
        EstimateParity(f, a, null, t);
        f.Battle.Store.Get<Weapon>(actor).Speed = 2; f.Prepare();
        Check(float.IsPositiveInfinity(f.Cost(a, null, t)), "non-intercepting slow projectile is unavailable");
    }
    private static void SuppressionChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var f = new Fixture<TStore>(factory(), LayerConfig(new DamageSettings { Missile = new(1.5f, 1) }));
        var actor = f.Spawn(0, ShipClass.Fighter, Vector2.Zero); var target = f.Spawn(1, ShipClass.Fighter, Vector2.Zero);
        f.Battle.Store.Get<Weapon>(actor) = LayerWeapon(WeaponKind.Plasma); LayerHealth(f, target, 90, 30);
        f.Battle.Store.Get<Weapon>(target) = LayerWeapon(WeaponKind.Cannon);
        f.Battle.Store.Get<Brain>(target).Mode = BrainMode.Attack; f.Battle.Store.Get<Brain>(target).Target = actor;
        f.Prepare(); var a = f.Index(actor); var t = f.Index(target); var plasma = f.Estimate(a, null, t);
        Check(plasma.Suppression > 0 && plasma.Suppression <= f.Battle.Config.Tactics.MaximumSuppressionBenefit && plasma.ExpectedLosses > 0,
            "bounded expected plasma heat benefit and separate threat forecast");
        f.Battle.Store.Get<Weapon>(actor).Kind = WeaponKind.Missile; f.Prepare(); var neutralHeat = f.Estimate(a, null, t);
        Parity(neutralHeat.KillSeconds, plasma.KillSeconds, "equal layer profiles have equal physical kill estimate");
        Parity(neutralHeat.ExpectedLosses, plasma.ExpectedLosses, "heat benefit not folded into loss or kill fields");
        Check(neutralHeat.Suppression == 0 && neutralHeat.Cost > plasma.Cost, "source profile heat, not Kind-specific bonus");
        f.Battle.Store.Get<Weapon>(actor).Kind = WeaponKind.Plasma; f.Battle.Store.Get<Brain>(target).Mode = BrainMode.Patrol; f.Prepare();
        Check(f.Estimate(a, null, t).Suppression == 0 && f.Estimate(a, null, t).ExpectedLosses == 0, "inactive patrol offers no firing suppression bonus");
        f.Battle.Store.Get<Brain>(target).Mode = BrainMode.Attack; f.Battle.Store.Get<Motion>(target).Velocity = new(100, 0);
        f.Battle.Store.Get<Collider>(target).Radius = 0.01f; f.Prepare();
        Check(f.Estimate(a, null, t).Suppression < plasma.Suppression, "low-probability heat within horizon is not guaranteed future suppression");
        EstimateParity(f, a, null, t);
    }
    private static void BoundedSelectionChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        var config = LayerConfig() with { Tactics = new TacticsSettings { LocalTargetCandidates = 1, GlobalTargetProbes = 0 } };
        using var f = new Fixture<TStore>(factory(), config, completeSelection: false);
        var actor = f.Spawn(0, ShipClass.Fighter, Vector2.Zero); f.Battle.Store.Get<Weapon>(actor) = LayerWeapon(WeaponKind.Cannon);
        var enemies = new List<EntityKey>();
        for (var i = 0; i < 10; i++) { var e = f.Spawn(1, ShipClass.Fighter, Vector2.Zero); LayerHealth(f, e, 0, 1000); enemies.Add(e); }
        f.Prepare(); var a = f.Index(actor); f.FillCandidates(a); var candidates = f.Field<List<int>>("targetCandidates").ToArray();
        Check(candidates.Length == 1, "bounded local candidate subset");
        var excluded = enemies.Select(f.Index).First(i => !candidates.Contains(i));
        var hull = f.Rows[excluded].Hull; hull.Current = 0.01f; f.Rows[excluded] = f.Rows[excluded] with { Hull = hull };
        var oracle = f.Oracle(); var actual = f.Select(a, null, new Brain { Target = EntityKey.None }, out var cost);
        Check(candidates.Any(i => f.Rows[i].Entity == actual), "selection stays inside admitted candidate subset");
        Parity(cost, candidates.Min(i => oracle.Cost(a, null, i)), "best only within bounded candidates");
        var global = oracle.Select(a, null, new Brain { Target = EntityKey.None }, out _, out _);
        Check(actual != global && global == f.Rows[excluded].Entity, "bounded search explicitly differs from full-snapshot optimum");
        f.Battle.Store.Get<Brain>(actor).Target = global; f.Battle.Store.Get<Brain>(actor).TargetLockUntilTick = 120; f.Prepare();
        Check(f.Select(a, null, f.Rows[a].Brain, out _) == global, "retained leased target considered outside sampled subset");
    }
    private delegate TacticalEstimate EstimateCall(int actor, Squad? squad, int target);
    private static void CanonicalImpactChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var f = new Fixture<TStore>(factory(), LayerConfig());
        var plasma = f.Spawn(0, ShipClass.Fighter, Vector2.Zero); var cannon = f.Spawn(0, ShipClass.Fighter, Vector2.Zero);
        var target = f.Spawn(1, ShipClass.Fighter, Vector2.Zero);
        f.Battle.Store.Get<Weapon>(plasma) = LayerWeapon(WeaponKind.Plasma);
        f.Battle.Store.Get<Weapon>(cannon) = LayerWeapon(WeaponKind.Cannon);
        f.Battle.Store.Get<Brain>(plasma).Mode = BrainMode.Attack; f.Battle.Store.Get<Brain>(cannon).Mode = BrainMode.Attack;
        LayerHealth(f, target, 6, 50); f.Prepare(); var p = f.Index(plasma); var c = f.Index(cannon); var t = f.Index(target);
        var first = new Squad { Count = 2, Leader = p }; first.Members[0] = p; first.Members[1] = c;
        var restored = new Squad { Count = 2, Leader = c }; restored.Members[0] = c; restored.Members[1] = p;
        var expected = f.Estimate(p, first, t); var actual = f.Estimate(c, restored, t);
        Bits(actual.KillSeconds, expected.KillSeconds, "simultaneous packet order is snapshot serial, not restored leader-first roster");
        Bits(actual.Cost, expected.Cost, "canonical composition cost is roster-permutation invariant");
        Parity(actual.KillSeconds, 39f / 15 + 1f / 60, "plasma lower serial consumes shield before kinetic hull packet");
        EstimateParity(f, c, restored, t);
        f.Battle.Store.Get<Weapon>(target) = LayerWeapon(WeaponKind.Cannon); f.Battle.Store.Get<Brain>(target).Mode = BrainMode.Attack;
        f.Prepare(); expected = f.Estimate(p, first, t); actual = f.Estimate(c, restored, t);
        Bits(actual.ExpectedLosses, expected.ExpectedLosses, "canonical loss accumulation");
        Bits(actual.Suppression, expected.Suppression, "group suppression opportunity does not depend on chosen leader");
        Bits(actual.Cost, expected.Cost, "canonical total cost with active target");
    }
    private static void ScoringAllocationChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var f = new Fixture<TStore>(factory(), LayerConfig());
        var actor = f.Spawn(0, ShipClass.Fighter, Vector2.Zero); var target = f.Spawn(1, ShipClass.Fighter, Vector2.Zero);
        f.Battle.Store.Get<Weapon>(actor) = LayerWeapon(WeaponKind.Cannon); LayerHealth(f, target, 40, 100); f.Prepare();
        var method = typeof(Battle<TStore>).GetMethod("EstimateTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var call = (EstimateCall)method.CreateDelegate(typeof(EstimateCall), f.Battle); var a = f.Index(actor); var t = f.Index(target);
        for (var i = 0; i < 32; i++) call(a, null, t);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++) call(a, null, t);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "warmed direct scorer performs no managed allocations");
    }
}
