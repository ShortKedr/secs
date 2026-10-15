using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    public static void ComparePorts()
    {
        var expected = Replay(static () => new SkecsStore());
        Check(Replay(static () => new LeoEcsLiteStore()), "Lite");
        Check(Replay(static () => new LeoEcsClassicStore()), "Classic");
        Check(Replay(static () => new MorpehStore()), "Morpeh");
        Check(Replay(static () => new StaticEcsStore()), "Static");
        void Check(ulong[] actual, string name)
        {
            for (var i = 0; i < expected.Length; i++) Require(actual[i] == expected[i], $"{name}: checksum at tick {(i + 1) * 300}: {actual[i]:X16} != {expected[i]:X16}");
            Console.WriteLine($"PASS: {name} == SKECS at all {actual.Length} checkpoints");
        }
    }
    private static ulong[] Replay<T>(Func<T> factory) where T : IEcsStore
    {
        using var battle = new Battle<T>(factory(), new BattleConfig { ShipsPerTeam = 16, Seed = 27 });
        var hashes = new ulong[10];
        for (var i = 0; i < 3000; i++)
        {
            battle.Step();
            if ((i + 1) % 300 == 0)
            {
                battle.Validate();
                Require(battle.Counters.SelfHits == 0, $"{battle.Framework}: no projectile self hits at tick {i + 1}");
                hashes[i / 300] = battle.Checksum();
            }
        }
        Console.WriteLine($"Replay {battle.Framework}: {hashes[^1]:X16}"); return hashes;
    }
    public static void Run()
    {
        FlatSpatialValidation.Run();
        HardwareOptimizationValidation.Run();
        SkecsMappingChecks();
        ComponentPassChecks();
        DampingLocalityValidation.Run();
        AiLocalityValidation.Run();
        AiLocalityValidation.RunDamage();
        SquadCompositionValidation.Run();
        TacticalScoringValidation.Run();
        WeaponDamageValidation.Run();
        var config = new BattleConfig { ShipsPerTeam = 16, Seed = 27 };
        ulong first;
        using (var battle = new Battle<SkecsStore>(new(), config))
        {
            var frame = new BattleFrame(); battle.Capture(frame);
            Require(frame.Ships.Select(s => s.Kind).Distinct().Count() == 11, "all eleven classes");
            var victim = frame.Ships.First(s => s.Kind == (int)ShipClass.Fighter);
            Require(battle.TryFindShip(victim.Serial, out var e), "ship lookup");
            var shield = battle.Store.Get<Shield>(e).Current;
            battle.Damage(e, 10, true, false);
            Require(battle.Store.Get<Shield>(e).Current == shield - 15 && battle.Store.Get<Heat>(e).Value > 0, "specialized plasma heats shield");
            for (var i = 0; i < 1800; i++) { battle.Step(); if (i % 100 == 0) battle.Validate(); }
            Require(battle.Counters.Shots > 0 && battle.Counters.Hits > 0 && battle.Counters.Builds > 0 && battle.Counters.ShipsDestroyed > 0, "live combat and production");
            Require(battle.Counters.StructuralAdds > 0 && battle.Counters.StructuralRemoves > 0 && battle.Counters.DebrisHits > 0, "real churn and damaging debris");
            first = battle.Checksum();
        }
        using (var battle = new Battle<SkecsStore>(new(), config))
        {
            var frame = new BattleFrame(); battle.Capture(frame); var victim = frame.Ships.First(s => s.Kind == (int)ShipClass.Fighter);
            battle.TryFindShip(victim.Serial, out var e); battle.Damage(e, 10, true, false);
            for (var i = 0; i < 1800; i++) battle.Step();
            Require(first == battle.Checksum(), "deterministic replay");
        }
        FleetCompositionChecks();
        FleetDeploymentValidation.Run();
        ObservationChecks();
        ColliderChecks();
        PhysicsChecks(); SpatialChecks(); FireControlChecks(); ProjectileChecks(); ProjectileOwnerChecks(); HeavyImpactChecks(); WarpReinforcementChecks(); MechanicsChecks(); TacticalChecks(); TacticalSpatialChecks(); ArmamentChecks(); LifecycleSpatialChecks(); StatisticsChecks();
        Console.WriteLine("PASS: classes, plasma heat, live combat, production, structural churn, damaging debris, deterministic replay, circle physics and analytic swept hits.");
    }
    private static void PhysicsChecks()
    {
        AnalyticSweepChecks();
        PhysicsKernelChecks();
        ContactCacheChecks();
        PreparedCcdChecks();
        PhysicsPrecisionChecks();
        using var physics = new PhysicsService();
        var shape = new Collider { Radius = 2, Mass = 20, ShapeCount = 6 };
        var id = physics.AddShip(1, new Transform { Position = new(10, 0) }, shape);
        Require(physics.Fixtures == 1, "one physical disk replaces former compound fixtures");
        Require(physics.Sweep(new(0, 0), new(20, 0), 0.1f, out var serial, out var impact) && serial == 1, "fast swept projectile without team filtering");
        Require(float.IsFinite(impact.X) && float.IsFinite(impact.Y) && impact.X > 0 && impact.X < 20, "swept impact lies on path");
        Require(physics.Sweep(new(10, 0), new(20, 0), 0.1f, out serial, out _) && serial == 1, "sweep includes starting body (owner)");
        var t = new Transform { Position = new(10, 0) }; var motion = new Motion { Velocity = new(40, 0) };
        physics.SetShip(id, t, motion, false);
        Require(!physics.Sweep(new(0, 0), new(20, 0), 0.1f, out _, out _), "docked collision disabled");
        physics.SetShip(id, t, motion, true); physics.Step(1f / 60);
        Require(MathF.Abs(physics.GetShip(id).Velocity.X - 40) < 0.01f, "zero physical drag");
        physics.RemoveShip(id); Require(physics.Bodies == 0 && physics.Fixtures == 0, "body cleanup");
    }
    private static void FireControlChecks()
    {
        CheckIntercept("crossing target", Vector2.Zero, Vector2.Zero, new(100, 0), new(0, 30), 60, 2);
        CheckIntercept("moving shooter", new(4, -3), new(0, 25), new(100, 0), Vector2.Zero, 60, 3);
        var commonMotion = CheckIntercept("shared shooter and target motion", Vector2.Zero, new(20, 30), new(100, 0), new(20, 30), 50, 2);
        Require(Vector2.Distance(commonMotion.Direction, Vector2.UnitX) < 0.0001f, "shared velocity requires no transverse lead");
        var stationary = CheckIntercept("stationary target", Vector2.Zero, Vector2.Zero, new(100, 0), Vector2.Zero, 50, 2);
        Require(MathF.Abs(stationary.InterceptTime - 1.96f) < 0.0001f && Vector2.Distance(stationary.Direction, Vector2.UnitX) < 0.0001f, "stationary muzzle offset time");
        var linear = CheckIntercept("equal-speed approaching target", Vector2.Zero, Vector2.Zero, new(100, 0), new(-50, 0), 50, 2);
        Require(MathF.Abs(linear.InterceptTime - 0.98f) < 0.0001f, "equal-speed linear solution");
        CheckFallback("equal-speed receding target", new(100, 0), new(50, 0), 50, 2);
        CheckFallback("faster crossing target", new(100, 0), new(0, 80), 50, 2);
        CheckFallback("zero projectile speed", new(100, 0), Vector2.Zero, 0, 2);
        CheckFallback("coincident positions", Vector2.Zero, Vector2.Zero, 50, 0);
        CheckFallback("target inside muzzle", new(1, 0), Vector2.Zero, 50, 2);

        const int samples = 16384;
        var impreciseCount = 0; var negative = 0; var positive = 0;
        var sum = 0f; var squaredSum = 0f;
        for (var i = 0; i < samples; i++)
        {
            var offset = FireControl.Spread(27, i % 97 + 1, i / 97, i % 2, out var imprecise);
            var repeat = FireControl.Spread(27, i % 97 + 1, i / 97, i % 2, out var repeatedImprecise);
            Require(offset == repeat && imprecise == repeatedImprecise, "spread reproducibility including decision");
            Require(float.IsFinite(offset) && MathF.Abs(offset) <= 0.03f, "bounded spread");
            if (!imprecise) { Require(offset == 0, "precise decisions have no spread"); continue; }
            impreciseCount++; sum += offset; squaredSum += offset * offset;
            if (offset < 0) negative++;
            if (offset > 0) positive++;
        }
        Require(impreciseCount > samples * 0.23f && impreciseCount < samples * 0.27f, "approximately 25 percent imprecise decisions");
        Require(negative > impreciseCount * 0.45f && positive > impreciseCount * 0.45f && MathF.Abs(sum / impreciseCount) < 0.001f, "spread symmetric around zero");
        Require(squaredSum / impreciseCount > 0.00027f && squaredSum / impreciseCount < 0.00033f, "uniform spread second moment");
        Console.WriteLine("PASS: crossing/shooter-motion/stationary intercepts, linear and fallback cases, deterministic 75/25 spread.");

        static AimSolution CheckIntercept(string name, Vector2 shooter, Vector2 shooterVelocity, Vector2 target, Vector2 targetVelocity, float speed, float muzzle)
        {
            var aim = FireControl.Predict(shooter, shooterVelocity, target, targetVelocity, speed, muzzle);
            Require(float.IsFinite(aim.InterceptTime) && aim.InterceptTime > 0 && MathF.Abs(aim.Direction.Length() - 1) < 0.0001f, name + " finite unit solution");
            var projectilePosition = shooter + aim.Direction * muzzle + (shooterVelocity + aim.Direction * speed) * aim.InterceptTime;
            var targetPosition = target + targetVelocity * aim.InterceptTime;
            Require(Vector2.Distance(projectilePosition, targetPosition) < 0.002f, name + " trajectory reaches target");
            return aim;
        }
        static void CheckFallback(string name, Vector2 target, Vector2 velocity, float speed, float muzzle)
        {
            var aim = FireControl.Predict(Vector2.Zero, Vector2.Zero, target, velocity, speed, muzzle);
            Require(aim.InterceptTime == 0 && float.IsFinite(aim.Direction.X) && float.IsFinite(aim.Direction.Y) && MathF.Abs(aim.Direction.Length() - 1) < 0.0001f, name + " finite direct fallback");
            if (target.LengthSquared() > 0) Require(Vector2.Distance(aim.Direction, Vector2.Normalize(target)) < 0.0001f, name + " direct direction");
        }
    }
    private static void ProjectileChecks()
    {
        CheckHit("enemy", false, false);
        CheckHit("friendly", true, false);
        CheckHit("self", true, true);
        using var battle = new Battle<SkecsStore>(new(), new BattleConfig { ShipsPerTeam = 16, Seed = 27 });
        var frame = FreezeBattle(battle);
        var shooter = frame.Ships.First(s =>
        {
            FireControl.Spread(27, s.Serial, 1, 0, out var imprecise);
            return !imprecise;
        });
        battle.TryFindShip(shooter.Serial, out var owner);
        battle.TryFindShip(frame.Ships.First(s => s.Serial != shooter.Serial).Serial, out var target);
        battle.Store.Get<Transform>(owner).Position = Vector2.Zero;
        battle.Store.Get<Transform>(target).Position = new(60, 0);
        var shooterVelocity = new Vector2(0, 12);
        battle.Store.Get<Motion>(owner) = new Motion { Velocity = shooterVelocity, MaxSpeed = 12 };
        battle.Store.Get<Brain>(owner).Mode = BrainMode.Attack;
        battle.Store.Get<Brain>(owner).Target = target;
        battle.Store.Get<Weapon>(owner) = new Weapon { Kind = WeaponKind.MachineGun, Burst = 3, Range = 100, Speed = 120, Damage = 1, Cooldown = 100 };
        var muzzle = battle.Store.Get<Collider>(owner).Radius + 0.8f;
        var aim = FireControl.Predict(Vector2.Zero, shooterVelocity, new(60, 0), Vector2.Zero, 120, muzzle);
        battle.Step(); battle.Capture(frame);
        Require(frame.Projectiles.Count == 3 && battle.Counters.Shots == 3, "controlled three-projectile burst");
        Require(battle.Counters.AimDecisions == 1 && battle.Counters.ImpreciseAimDecisions == 0, "one precise aim decision per burst, not per projectile");
        foreach (var projectile in frame.Projectiles)
        {
            Require(Vector2.Distance(projectile.Velocity, shooterVelocity + aim.Direction * 120) < 0.002f, "initial projectile velocity inherits shooter motion without spread");
        }
        Console.WriteLine("PASS: enemy/friendly projectile damage, owner immunity, hit counters, burst aim decisions and inherited projectile velocity.");

        static void CheckHit(string name, bool friendly, bool self)
        {
            using var battle = new Battle<SkecsStore>(new(), new BattleConfig { ShipsPerTeam = 16 });
            var frame = FreezeBattle(battle);
            var ownerShip = frame.Ships.First();
            battle.TryFindShip(ownerShip.Serial, out var owner);
            var team = battle.Store.Get<Identity>(owner).Team;
            var victimShip = self ? ownerShip : frame.Ships.First(s => s.Serial != ownerShip.Serial &&
                battle.TryFindShip(s.Serial, out var candidate) && (battle.Store.Get<Identity>(candidate).Team == team) == friendly);
            battle.TryFindShip(victimShip.Serial, out var victim);
            battle.Store.Get<Transform>(victim).Position = Vector2.Zero;
            battle.Store.Get<Shield>(victim).Current = 0;
            battle.Store.Get<Shield>(victim).RegenPerSecond = 0;
            var before = battle.Store.Get<Hull>(victim).Current;
            var armor = battle.Store.Get<Hull>(victim).Armor;
            // Synthetic serials stay confined to this disposable battle; canonical Validate is deliberately not called.
            var projectile = battle.Store.Create();
            battle.Store.Set(projectile, new Transform { Position = new(-20, 0), Previous = new(-20, 0) });
            battle.Store.Set(projectile, new Motion { Velocity = new(2400, 0) });
            battle.Store.Set(projectile, new Projectile { Serial = 1000000, Owner = owner, Target = owner,
                Kind = WeaponKind.MachineGun, Damage = 10, Radius = 0.1f, ExpiresTick = 100 });
            battle.Store.Commit(); battle.Step();
            if (self)
            {
                Require(battle.Store.Get<Hull>(victim).Current == before, "self projectile leaves own hull untouched");
                Require(battle.Store.Alive(projectile), "self projectile survives crossing owner");
                Require(battle.Counters.Hits == 0 && battle.Counters.FriendlyFireHits == 0 && battle.Counters.SelfHits == 0,
                    "self exclusion produces no hit telemetry");
                return;
            }
            Require(MathF.Abs(battle.Store.Get<Hull>(victim).Current - (before - 15 * (1 - armor))) < 0.002f, name + " specialized projectile damages actual swept victim regardless of telemetry target");
            Require(!battle.Store.Alive(projectile) && battle.Counters.Hits == 1, name + " projectile consumed on hit");
            Require(battle.Counters.FriendlyFireHits == (friendly ? 1 : 0) && battle.Counters.SelfHits == (self ? 1 : 0), name + " friendly/self telemetry");
        }
    }
    private static BattleFrame FreezeBattle(Battle<SkecsStore> battle)
    {
        var frame = new BattleFrame(); battle.Capture(frame);
        for (var i = 0; i < frame.Ships.Count; i++)
        {
            battle.TryFindShip(frame.Ships[i].Serial, out var e);
            var position = new Vector2(80 + i % 8 * 30, 80 + i / 8 * 30);
            battle.Store.Get<Transform>(e) = new Transform { Position = position, Previous = position };
            battle.Store.Get<Motion>(e) = default;
            battle.Store.Get<Brain>(e) = new Brain { Mode = BrainMode.Patrol, NextDecisionTick = int.MaxValue,
                Target = EntityKey.None, Home = EntityKey.None, SquadLeader = EntityKey.None, CoverAlly = EntityKey.None };
            battle.Store.Get<Inventory>(e).NextUseTick = int.MaxValue;
            battle.Store.Get<Weapon>(e).NextFireTick = int.MaxValue;
            if (battle.Store.Has<SecondaryWeapon>(e)) battle.Store.Get<SecondaryWeapon>(e).Value.NextFireTick = int.MaxValue;
            if (battle.Store.Has<Carrier>(e)) battle.Store.Get<Carrier>(e).NextBuildTick = int.MaxValue;
        }
        return frame;
    }
    private static void MechanicsChecks()
    {
        using var battle = new Battle<SkecsStore>(new(), new BattleConfig { ShipsPerTeam = 16 });
        var frame = new BattleFrame(); battle.Capture(frame);
        var ship = frame.Ships.First(s => s.Kind == (int)ShipClass.Fighter);
        battle.TryFindShip(ship.Serial, out var e);
        var home = battle.Store.Get<Brain>(e).Home;
        battle.Store.Get<Transform>(home).Position = new(-180, -180);
        battle.Store.Get<Transform>(e).Position = new(-178, -180);
        battle.Store.Get<Brain>(e).NextDecisionTick = 0;
        battle.Store.Get<Hull>(e).Current = battle.Store.Get<Hull>(e).Maximum * 0.2f;
        battle.Store.Get<Inventory>(e).NextUseTick = int.MaxValue;
        battle.Step(); Require(battle.Store.Has<Docked>(e), "damaged AI ship docks");
        var before = battle.Store.Get<Hull>(e).Current;
        for (var i = 0; i < 60; i++) battle.Step();
        Require(battle.Store.Get<Hull>(e).Current > before && battle.Counters.Repairs > 0, "docking repairs hull");
        battle.Store.Get<Hull>(e).Current = battle.Store.Get<Hull>(e).Maximum;
        for (var i = 0; i < 180; i++) battle.Step();
        Require(!battle.Store.Has<Docked>(e), "repaired AI ship undocks");
        ref var heat = ref battle.Store.Get<Heat>(e); heat.Value = heat.Limit * 1.5f;
        battle.Step(); Require(battle.Store.Has<Overheated>(e), "overheat transition");
        battle.Store.Get<Heat>(e).Value = 0; battle.Step(); Require(!battle.Store.Has<Overheated>(e), "cooling exits overheat");
        ref var shield = ref battle.Store.Get<Shield>(e); shield.Current = shield.Maximum * 0.5f; shield.LastHitTick = battle.Tick;
        var shieldBefore = shield.Current;
        battle.Step(); Require(battle.Store.Get<Shield>(e).Current <= shieldBefore, "regen waits out of combat");
        battle.Store.Get<Shield>(e).LastHitTick = battle.Tick - 1000;
        battle.Step(); Require(battle.Store.Get<Shield>(e).Current > shieldBefore, "out-of-combat shield regeneration");
        ref var inventory = ref battle.Store.Get<Inventory>(e);
        inventory.Items[0] = new InventoryItem { Kind = ItemKind.ShieldCell, Count = 1 }; inventory.NextUseTick = 0;
        battle.Store.Get<Shield>(e).Current = 0;
        battle.Step(); Require(battle.Store.Get<Inventory>(e).Items[0].Count == 0 && battle.Counters.ItemUses > 0, "shield cell consumption");
        inventory = ref battle.Store.Get<Inventory>(e);
        inventory.Items[0] = new InventoryItem { Kind = ItemKind.HeatSink, Count = 1 }; inventory.NextUseTick = 0;
        battle.Store.Get<Heat>(e).Value = battle.Store.Get<Heat>(e).Limit;
        battle.Step(); Require(battle.Store.Get<Inventory>(e).Items[0].Count == 0, "heat sink consumption");
        battle.Validate();
        var health = battle.Store.Get<Hull>(e).Current;
        battle.Store.Get<Hull>(e).Current = float.NaN;
        var rejected = false;
        try { battle.Validate(); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "NaN health rejected"); battle.Store.Get<Hull>(e).Current = health;
        Console.WriteLine("PASS: AI docking/repair/undocking, overheat/cooling, shield regen delay, shield cells and heat sinks.");
    }
    private static void Require(bool pass, string name) { if (!pass) throw new InvalidOperationException("Validation failed: " + name); }
}
