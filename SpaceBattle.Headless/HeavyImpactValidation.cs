using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    private static void HeavyImpactChecks()
    {
        var expected = HeavyImpactStoreChecks(static () => new SkecsStore());
        Check(HeavyImpactStoreChecks(static () => new SkecsStore()), "SKECS repeat");
        Check(HeavyImpactStoreChecks(static () => new LeoEcsLiteStore()), "Lite");
        Check(HeavyImpactStoreChecks(static () => new LeoEcsClassicStore()), "Classic");
        Check(HeavyImpactStoreChecks(static () => new MorpehStore()), "Morpeh");
        Check(HeavyImpactStoreChecks(static () => new StaticEcsStore()), "Static");
        Console.WriteLine("PASS: heavy swept impacts, visible core and dispersing sparks, separate death explosion, misses/owner exclusion, particle budgets, captured animation/expiry without splash, same-seed all-store parity.");

        void Check(HeavyImpactTrace actual, string name)
        {
            Require(expected.Hashes.SequenceEqual(actual.Hashes), name + " heavy impact per-tick checksum parity");
            Require(expected.Frames.Count == actual.Frames.Count, name + " heavy impact frame count parity");
            for (var i = 0; i < expected.Frames.Count; i++)
                Require(expected.Frames[i].SequenceEqual(actual.Frames[i]),
                    name + " heavy impact captured particle parity at tick " + (i + 1));
        }
    }

    private sealed class HeavyImpactTrace
    {
        public List<ulong> Hashes { get; } = new();
        public List<RenderEntity[]> Frames { get; } = new();
    }

    private static HeavyImpactTrace HeavyImpactStoreChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        foreach (var kind in Enum.GetValues<WeaponKind>()) HeavyImpactHitCheck(factory, kind, lethal: false);
        HeavyImpactHitCheck(factory, WeaponKind.HeavyCannon, lethal: true);
        HeavyImpactMissCheck(factory, expireOnSweep: false);
        HeavyImpactMissCheck(factory, expireOnSweep: true);
        HeavyImpactOwnerCheck(factory);
        HeavyImpactBudgetCheck(factory, multiplier: 0, maxParticles: 1);
        HeavyImpactBudgetCheck(factory, multiplier: 1, maxParticles: 1);
        HeavyImpactBudgetCheck(factory, multiplier: 2, maxParticles: 32000);
        return HeavyImpactAnimationCheck(factory);
    }

    private static Battle<TStore> HeavyImpactBattle<TStore>(Func<TStore> factory, int multiplier = 1,
        int maxParticles = 32000) where TStore : IEcsStore
    {
        if (multiplier == 0)
        {
            var quiet = LifecycleBattle(factory);
            LifecycleFreezeMounts(quiet);
            return quiet;
        }
        var battle = new Battle<TStore>(factory(), new BattleConfig
        {
            ShipsPerTeam = 8, Seed = 27, ParticleMultiplier = multiplier,
            MaxParticles = maxParticles, ReplenishCarriers = false
        });
        var frame = new BattleFrame();
        battle.Capture(frame);
        for (var i = 0; i < frame.Ships.Count; i++)
        {
            Require(battle.TryFindShip(frame.Ships[i].Serial, out var e), "heavy fixture ship lookup");
            var position = new Vector2(1000 + i * 60, 1000);
            battle.Store.Get<Transform>(e) = new Transform { Position = position, Previous = position };
            LifecycleFreezeShip(battle, e);
            battle.Store.Get<Inventory>(e).NextUseTick = int.MaxValue;
        }
        LifecycleFreezeMounts(battle);
        return battle;
    }

    private static BattleFrame HeavyImpactFrame<TStore>(Battle<TStore> battle) where TStore : IEcsStore
    {
        var cached = new BattleFrame();
        var refreshed = new BattleFrame();
        battle.Capture(cached, refreshEntities: false);
        battle.Capture(refreshed);
        Require(cached.Particles.SequenceEqual(refreshed.Particles), battle.Framework + " heavy cached/default Capture parity");
        return refreshed;
    }

    private static void HeavyImpactHitCheck<TStore>(Func<TStore> factory, WeaponKind kind, bool lethal)
        where TStore : IEcsStore
    {
        using var battle = HeavyImpactBattle(factory);
        var owner = OwnerActor(battle, 0, ShipClass.Fighter, new(-60, 0));
        var target = OwnerActor(battle, 1, ShipClass.Fighter, Vector2.Zero);
        if (lethal) battle.Store.Get<Hull>(target).Current = 5;
        var round = OwnerRound(battle, owner, kind, new(-20, 0), new(2400, 0));
        battle.Step();
        var frame = HeavyImpactFrame(battle);
        var label = battle.Framework + " " + kind + (lethal ? " lethal" : " surviving");
        Require(!battle.Store.Alive(round) && battle.ProjectileCount == 0 && battle.Counters.Hits == 1,
            label + " swept collision consumes round once");
        Require(battle.Store.Get<Hull>(owner).Current == 10000, label + " owner undamaged");
        Require(frame.Particles.Any(p => p.Kind == HeavyImpactEffect.Kind) == (kind == WeaponKind.HeavyCannon),
            label + " only heavy collisions produce kind3");
        Require(frame.Particles.Any(p => p.Kind == 2) == lethal,
            label + " kind2 belongs only to ship destruction");
        Require(battle.Counters.ShipsDestroyed == (lethal ? 1 : 0) && battle.Counters.Explosions == (lethal ? 1 : 0),
            label + " impact does not masquerade as death explosion");
        if (lethal)
            Require(!battle.Store.Alive(target), label + " target destroyed");
        else
            Require(battle.Store.Alive(target) && (kind is WeaponKind.Cannon or WeaponKind.Missile
                ? battle.Store.Get<Hull>(target).Current <= (kind == WeaponKind.Cannon ? 9985 : 9990)
                : battle.Store.Get<Hull>(target).Current == (kind is WeaponKind.Plasma ? 9990 : 9985)),
                label + " nonlethal collision damage (ordinary explosive rounds may add fragments)");
        if (kind == WeaponKind.HeavyCannon)
        {
            var particles = HeavyImpactParticles(battle);
            var core = particles.Single(p => p.Value.Kind == HeavyImpactEffect.Kind && p.Value.Size >= 3);
            Require(core.Value.Size == HeavyImpactEffect.CoreRadius(10), label + " damage-scaled visible primary core");
            Require(frame.Particles.Single(p => p.Serial == core.Value.Serial).Color.W > 0,
                label + " primary core captured visibly");
            Require(particles.Count(p => p.Value.Kind == HeavyImpactEffect.Kind) == 12,
                label + " unchanged twelve-particle impact budget");
        }
    }

    private static void HeavyImpactMissCheck<TStore>(Func<TStore> factory, bool expireOnSweep)
        where TStore : IEcsStore
    {
        using var battle = HeavyImpactBattle(factory);
        var owner = OwnerActor(battle, 0, ShipClass.Fighter, new(-60, 0));
        var target = OwnerActor(battle, 1, ShipClass.Fighter, Vector2.Zero);
        var y = expireOnSweep ? 0 : 30;
        var round = OwnerRound(battle, owner, WeaponKind.HeavyCannon, new(-20, y), new(2400, 0),
            expires: expireOnSweep ? 1 : 3);
        battle.Step();
        Require(battle.Store.Alive(round) != expireOnSweep, battle.Framework + " heavy miss/expiry round lifetime");
        Require(battle.Counters.Hits == 0 && HeavyImpactFrame(battle).Particles.Count == 0,
            battle.Framework + " miss or expiry before potential sweep creates no impact");
        while (battle.Tick < 3) battle.Step();
        Require(!battle.Store.Alive(round) && battle.ParticleCount == 0 && battle.Counters.Hits == 0 &&
            battle.Store.Get<Hull>(target).Current == 10000, battle.Framework + " heavy miss expires without damage/effects");
    }

    private static void HeavyImpactOwnerCheck<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var battle = HeavyImpactBattle(factory);
        var owner = OwnerActor(battle, 0, ShipClass.Frigate, Vector2.Zero);
        var round = OwnerRound(battle, owner, WeaponKind.HeavyCannon, new(-20, 0), new(2400, 0), expires: 3);
        battle.Step();
        Require(battle.Store.Alive(round) && battle.Store.Get<Transform>(round).Position.X > 0 &&
            battle.Counters.Hits == 0 && HeavyImpactFrame(battle).Particles.Count == 0,
            battle.Framework + " heavy owner crossing is not a visual impact");
        battle.Step(); battle.Step();
        Require(!battle.Store.Alive(round) && battle.Store.Get<Hull>(owner).Current == 10000 &&
            battle.Counters.SelfHits == 0 && battle.Counters.FriendlyFireHits == 0 && battle.ParticleCount == 0,
            battle.Framework + " owner-excluded heavy round expires quietly");
    }

    private static void HeavyImpactBudgetCheck<TStore>(Func<TStore> factory, int multiplier, int maxParticles)
        where TStore : IEcsStore
    {
        using var battle = HeavyImpactBattle(factory, multiplier, maxParticles);
        var owner = OwnerActor(battle, 0, ShipClass.Fighter, new(-60, 0));
        var target = OwnerActor(battle, 1, ShipClass.Fighter, Vector2.Zero);
        OwnerRound(battle, owner, WeaponKind.HeavyCannon, new(-20, 0), new(2400, 0));
        battle.Step();
        var frame = HeavyImpactFrame(battle);
        var count = Math.Min(12 * multiplier, maxParticles);
        Require(battle.Counters.Hits == 1 && battle.Store.Get<Hull>(target).Current == 9985,
            battle.Framework + " heavy damage independent of particle budget");
        Require(frame.Particles.Count == count && battle.ParticleCount == count &&
            frame.Particles.All(p => p.Kind == HeavyImpactEffect.Kind), battle.Framework + " heavy bounded multiplier count");
        if (count > 0)
            Require(HeavyImpactParticles(battle).Count(p => p.Value.Size >= 3) == 1,
                battle.Framework + " capped/multiplied effect preserves exactly one primary core");
        if (maxParticles == 1 && multiplier > 0)
        {
            OwnerRound(battle, owner, WeaponKind.HeavyCannon, new(-20, 0), new(2400, 0));
            battle.Step();
            Require(battle.Store.Get<Hull>(target).Current == 9970 && battle.Counters.Hits == 2 &&
                HeavyImpactFrame(battle).Particles.Count == 1 && battle.ParticleCount == 1,
                battle.Framework + " already-full particle cap cannot suppress subsequent heavy damage");
        }
    }

    private static HeavyImpactTrace HeavyImpactAnimationCheck<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var battle = HeavyImpactBattle(factory);
        var owner = OwnerActor(battle, 0, ShipClass.Fighter, new(-60, 0));
        var target = OwnerActor(battle, 1, ShipClass.Fighter, Vector2.Zero);
        var behind = OwnerActor(battle, 1, ShipClass.Fighter, new(12, 0));
        var nearby = OwnerActor(battle, 0, ShipClass.Fighter, new(0, 8));
        OwnerRound(battle, owner, WeaponKind.HeavyCannon, new(-20, 0), new(2400, 0));
        battle.Step();
        var particles = HeavyImpactParticles(battle);
        Require(HeavyImpactEffect.LifetimeTicks == 36 && particles.Count == 12 && particles.All(p => p.Value.Kind == HeavyImpactEffect.Kind &&
            p.Value.ExpiresTick == battle.Tick + HeavyImpactEffect.LifetimeTicks),
            battle.Framework + " all heavy particles share fixed bounded lifetime");
        var core = particles.Single(p => p.Value.Size >= 3);
        var origin = battle.Store.Get<Transform>(core.Entity).Position;
        var sparks = particles.Where(p => p.Entity != core.Entity).ToArray();
        Require(battle.Store.Get<Motion>(core.Entity).Velocity == Vector2.Zero && origin.X < 0 &&
            MathF.Abs(origin.Y) < 0.001f && origin.X > -20,
            battle.Framework + " stationary core anchored at actual swept contact, not target center");
        Require(sparks.All(p => p.Value.Size > 0 && p.Value.Size < core.Value.Size) &&
            sparks.Any(p => battle.Store.Get<Motion>(p.Entity).Velocity.LengthSquared() > 0),
            battle.Framework + " smaller moving heavy sparks");
        var trace = new HeavyImpactTrace();
        var initial = HeavyImpactFrame(battle).Particles.Single(p => p.Serial == core.Value.Serial);
        var expiry = core.Value.ExpiresTick;
        while (true)
        {
            var frame = HeavyImpactFrame(battle);
            trace.Hashes.Add(battle.Checksum());
            trace.Frames.Add(frame.Particles.ToArray());
            Require(battle.Store.Get<Hull>(target).Current == 9985 && battle.Store.Get<Hull>(owner).Current == 10000 &&
                battle.Store.Get<Hull>(behind).Current == 10000 && battle.Store.Get<Hull>(nearby).Current == 10000 &&
                battle.Counters.Hits == 1 && battle.Counters.DebrisHits == 0 && battle.Counters.ShipsDestroyed == 0 &&
                battle.DebrisCount == 0 && battle.ProjectileCount == 0,
                battle.Framework + " heavy animation adds neither damage nor splash/debris");
            if (battle.Tick >= expiry)
            {
                Require(frame.Particles.Count == 0 && battle.ParticleCount == 0 && !battle.Store.Alive(core.Entity),
                    battle.Framework + " heavy effect removed exactly at fixed deadline");
                break;
            }
            var captured = frame.Particles.Single(p => p.Serial == core.Value.Serial);
            HeavyImpactEffect.Present(core.Value, battle.Tick, out var radius, out var alpha);
            Require(captured.Radius == radius && captured.Color.W == alpha && captured.Position == origin &&
                float.IsFinite(radius) && radius > 0 && alpha > 0 && alpha <= 1,
                battle.Framework + " shared Capture core presentation throughout lifetime");
            if (battle.Tick == expiry - HeavyImpactEffect.LifetimeTicks + 6)
                Require(captured.Radius > initial.Radius && sparks.Any(p =>
                    Vector2.DistanceSquared(battle.Store.Get<Transform>(p.Entity).Position, origin) > 0.001f),
                    battle.Framework + " captured core expands and sparks disperse");
            if (battle.Tick == expiry - 1)
                Require(captured.Radius > initial.Radius && captured.Color.W < initial.Color.W * 0.01f,
                    battle.Framework + " expanded captured core fades before expiry");
            battle.Step();
        }
        battle.Step();
        Require(HeavyImpactFrame(battle).Particles.Count == 0 && battle.Counters.Hits == 1,
            battle.Framework + " expired impact does not respawn");
        return trace;
    }

    private static List<(EntityKey Entity, Particle Value)> HeavyImpactParticles<TStore>(Battle<TStore> battle)
        where TStore : IEcsStore
    {
        var collector = new HeavyImpactParticleCollector { Values = new() };
        battle.Store.ForEach<Particle, HeavyImpactParticleCollector>(ref collector);
        collector.Values.Sort(static (a, b) => a.Value.Serial.CompareTo(b.Value.Serial));
        return collector.Values;
    }

    private struct HeavyImpactParticleCollector : IProcessor<Particle>
    {
        public List<(EntityKey Entity, Particle Value)> Values;
        public void Execute(EntityKey e, ref Particle particle) => Values.Add((e, particle));
    }
}
