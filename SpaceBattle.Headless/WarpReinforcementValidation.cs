using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    private static void WarpReinforcementChecks()
    {
        var defaults = new BattleConfig();
        Require(!defaults.EndlessReinforcements && defaults.ReinforcementThresholdFraction == .25f &&
            defaults.ReinforcementWaveFraction == .65f && defaults.ReinforcementWarmupTicks == 90 &&
            defaults.ReinforcementSpawnIntervalTicks == 4 && defaults.ReinforcementCooldownTicks == 600,
            "warp reinforcement opt-in and timing defaults");
        WarpModeChecks();
        WarpStrengthAndCapacityChecks();
        WarpProductionCheck();
        WarpLegacyRespawnCheck();
        WarpBlockedArrivalCheck();
        var expected = WarpRepeatedWaves(static () => new SkecsStore(), 1);
        Require(expected.SequenceEqual(WarpRepeatedWaves(static () => new SkecsStore(), 1)),
            "warp deterministic same-seed repeat");
        Check(WarpRepeatedWaves(static () => new LeoEcsLiteStore(), 1), "Lite");
        Check(WarpRepeatedWaves(static () => new LeoEcsClassicStore(), 1), "Classic");
        Check(WarpRepeatedWaves(static () => new MorpehStore(), 1), "Morpeh");
        Check(WarpRepeatedWaves(static () => new StaticEcsStore(), 1), "Static");
        WarpRepeatedWaves(static () => new SkecsStore(), 0);
        WarpParticleBudgetCheck();
        Console.WriteLine("PASS: endless warp opt-in, count/strength triggers, warning, stagger, corners, survivors, caps, repeat waves, effects and all five ports.");

        void Check(ulong[] actual, string name) => Require(expected.SequenceEqual(actual),
            $"{name}: warp reinforcement replay matches SKECS at every checkpoint");
    }

    private static BattleConfig WarpConfig(int particles = 0) => new()
    {
        ShipsPerTeam = 8, CarriersPerTeam = 2, Seed = 27,
        EndlessReinforcements = true, ReplenishCarriers = true, ParticleMultiplier = particles,
        ReinforcementCooldownTicks = 120
    };

    private static Battle<TStore> WarpBattle<TStore>(Func<TStore> factory, BattleConfig config)
        where TStore : IEcsStore
    {
        var battle = new Battle<TStore>(factory(), config);
        WarpFreeze(battle);
        return battle;
    }

    private static BattleFrame WarpFrame<TStore>(Battle<TStore> battle) where TStore : IEcsStore
    {
        var frame = new BattleFrame();
        battle.Capture(frame);
        return frame;
    }

    private static void WarpFreeze<TStore>(Battle<TStore> battle) where TStore : IEcsStore
    {
        foreach (var ship in WarpFrame(battle).Ships)
        {
            Require(battle.TryFindShip(ship.Serial, out var e), "warp fixture ship lookup");
            LifecycleFreezeShip(battle, e);
            battle.Store.Get<Inventory>(e).NextUseTick = int.MaxValue;
            battle.Store.Get<Shield>(e).RegenPerSecond = 0;
            // Separate even newly arrived ships after recording their actual arrival position.
            var position = new Vector2(1000 + ship.Serial * 60, 1000 + ship.Team * 100);
            battle.Store.Get<Transform>(e) = new Transform { Position = position, Previous = position };
        }
        LifecycleFreezeMounts(battle);
    }

    private static BattleFrame WarpStep<TStore>(Battle<TStore> battle) where TStore : IEcsStore
    {
        battle.Step();
        var frame = WarpFrame(battle);
        WarpCaps(battle, frame);
        WarpFreeze(battle);
        return frame;
    }

    private static void WarpUntil<TStore>(Battle<TStore> battle, int tick) where TStore : IEcsStore
    {
        Require(tick >= battle.Tick && tick - battle.Tick <= 2200, "warp fixture bounded advance");
        while (battle.Tick < tick) WarpStep(battle);
    }

    private static void WarpCaps<TStore>(Battle<TStore> battle, BattleFrame frame) where TStore : IEcsStore
    {
        for (var team = 0; team < 2; team++)
        {
            var ships = frame.Ships.Where(s => s.Team == team).ToArray();
            Require(ships.Length <= battle.Config.ShipsPerTeam + battle.Config.CarriersPerTeam,
                $"{battle.Framework}: warp team {team} population cap at {battle.Tick}");
            Require(ships.Count(s => ShipCatalog.IsCarrier((ShipClass)s.Kind)) <= battle.Config.CarriersPerTeam,
                $"{battle.Framework}: warp team {team} carrier cap at {battle.Tick}");
        }
        Require(frame.Particles.Count <= battle.Config.MaxParticles && battle.ParticleCount <= battle.Config.MaxParticles,
            $"{battle.Framework}: warp particle budget at {battle.Tick}");
        Require(frame.Ships.Select(s => s.Serial).Distinct().Count() == frame.Ships.Count,
            $"{battle.Framework}: unique live warp ship serials");
        var clock = new WarpParticleClock { Tick = battle.Tick, Valid = true };
        battle.Store.ForEach<Particle, WarpParticleClock>(ref clock);
        Require(clock.Valid, "warp effects carry valid explicit start ticks");
        if (battle.Config.ParticleMultiplier == 0)
            Require(frame.Particles.Count == 0, "warp multiplier zero disables effects");
    }

    private struct WarpParticleClock : IProcessor<Particle>
    {
        public int Tick;
        public bool Valid;
        public void Execute(EntityKey e, ref Particle particle)
        {
            if (particle.Kind is >= 4 and <= 7)
                Valid &= particle.StartTick > 0 && particle.StartTick <= Tick && particle.ExpiresTick > particle.StartTick;
        }
    }

    private static int[] WarpDeplete<TStore>(Battle<TStore> battle, params int[] teams) where TStore : IEcsStore
    {
        var frame = WarpFrame(battle);
        var survivors = frame.Ships.Where(s => teams.Contains(s.Team) && s.Kind == (int)ShipClass.Fighter)
            .GroupBy(s => s.Team).Select(g => g.First().Serial).ToArray();
        Require(survivors.Length == teams.Length, "warp depletion keeps one fighter per requested team");
        foreach (var ship in frame.Ships.Where(s => teams.Contains(s.Team) && !survivors.Contains(s.Serial)))
        {
            Require(battle.TryFindShip(ship.Serial, out var e), "warp depletion ship lookup");
            battle.Damage(e, 1000000, false, false);
        }
        WarpStep(battle);
        return survivors;
    }

    private static void WarpModeChecks()
    {
        using (var off = WarpBattle(static () => new SkecsStore(), WarpConfig() with
               { EndlessReinforcements = false, ReplenishCarriers = false }))
        {
            var survivors = WarpDeplete(off, 0);
            WarpUntil(off, 240);
            Require(off.Counters.ReinforcementWaves == 0 && off.Counters.ReinforcementShips == 0 &&
                WarpFrame(off).Ships.Count(s => s.Team == 0) == survivors.Length,
                "warp disabled does not schedule waves or spawn ships");
        }
        using (var healthy = WarpBattle(static () => new SkecsStore(), WarpConfig()))
        {
            WarpUntil(healthy, 240);
            Require(healthy.Counters.ReinforcementWaves == 0 && healthy.Counters.ReinforcementShips == 0,
                "warp healthy fleets do not schedule reinforcement");
        }
        using var on = WarpBattle(static () => new SkecsStore(), WarpConfig(1));
        var original = WarpFrame(on).Ships.ToArray();
        var survivorsOn = WarpDeplete(on, 0);
        var survivor = survivorsOn.Single();
        Require(on.TryFindShip(survivor, out var survivorEntity), "warp survivor entity lookup");
        var survivorHull = on.Store.Get<Hull>(survivorEntity).Current;
        WarpUntil(on, 59);
        Require(on.Counters.ReinforcementWaves == 0, "warp scheduler defers until tick 60");
        var warning = WarpStep(on);
        Require(on.Counters.ReinforcementWaves == 1 && on.Counters.ReinforcementShips == 0,
            "warp count depletion schedules only the depleted team");
        Require(warning.Particles.Any(p => p.Kind is 4 or 5) && !warning.Particles.Any(p => p.Kind is 6 or 7),
            "warp portal warning precedes arrival effects");
        var start = on.Tick;
        WarpUntil(on, start + on.Config.ReinforcementWarmupTicks - 1);
        Require(on.Counters.ReinforcementWaves == 1 && on.Counters.ReinforcementShips == 0 &&
            WarpFrame(on).Ships.Count(s => s.Team == 0) == 1,
            "warp pending wave is unique and cannot spawn before warmup");
        var known = WarpFrame(on).Ships.Select(s => s.Serial).ToHashSet();
        var arrivals = new List<(int Tick, RenderEntity Ship)>();
        var sawArrivalEffect = false;
        var end = start + on.Config.ReinforcementWarmupTicks + 12 * on.Config.ReinforcementSpawnIntervalTicks;
        while (on.Tick < end)
        {
            var frame = WarpStep(on);
            sawArrivalEffect |= frame.Particles.Any(p => p.Kind is 6 or 7);
            foreach (var ship in frame.Ships.Where(s => known.Add(s.Serial))) arrivals.Add((on.Tick, ship));
        }
        Require(arrivals.Count > 1 && arrivals.All(a => a.Ship.Team == 0 && a.Ship.Serial > original.Max(s => s.Serial)),
            "warp creates fresh serials belonging only to depleted team");
        Require(arrivals.Zip(arrivals.Skip(1), (a, b) => b.Tick - a.Tick)
            .All(gap => gap >= on.Config.ReinforcementSpawnIntervalTicks), "warp arrivals are staggered");
        Require(sawArrivalEffect && on.Counters.ReinforcementShips == arrivals.Count,
            "warp arrival effects and exact ship telemetry");
        Require(on.Store.Alive(survivorEntity) && on.Store.Get<Identity>(survivorEntity).Serial == survivor &&
            on.Store.Get<Hull>(survivorEntity).Current == survivorHull,
            "warp preserves surviving entity generation, serial and hull");
        Require(WarpFrame(on).Ships.Where(s => s.Team == 1).Select(s => s.Serial)
            .SequenceEqual(original.Where(s => s.Team == 1).Select(s => s.Serial)), "warp does not reset opposing fleet");
        on.Validate();
    }

    private static void WarpStrengthAndCapacityChecks()
    {
        using var battle = WarpBattle(static () => new SkecsStore(), WarpConfig());
        var initial = WarpFrame(battle);
        var victim = initial.Ships.First(s => s.Team == 0 && s.Kind == (int)ShipClass.Interceptor);
        Require(battle.TryFindShip(victim.Serial, out var doomed), "warp strength victim lookup");
        battle.Damage(doomed, 1000000, false, false);
        WarpStep(battle);
        foreach (var ship in initial.Ships.Where(s => s.Team == 0 && s.Kind >= (int)ShipClass.Corvette))
        {
            Require(battle.TryFindShip(ship.Serial, out var e), "warp strength fixture lookup");
            battle.Store.Get<Hull>(e).Current = battle.Store.Get<Hull>(e).Maximum * .01f;
            battle.Store.Get<Shield>(e).Current = 0;
        }
        WarpUntil(battle, 60);
        Require(WarpFrame(battle).Ships.Count == initial.Ships.Count - 1 && battle.Counters.ReinforcementWaves == 1,
            "warp weighted low strength triggers despite healthy population count");
        WarpUntil(battle, 180);
        Require(battle.Counters.ReinforcementShips == 1 && WarpFrame(battle).Ships.Count == initial.Ships.Count,
            "warp low-strength wave is limited to the single available population slot");
        WarpUntil(battle, 360);
        Require(battle.Counters.ReinforcementWaves == 1 && battle.Counters.ReinforcementShips == 1,
            "warp full low-strength fleet defers additional waves instead of exceeding cap");
        battle.Validate();
    }

    private static void WarpProductionCheck()
    {
        using var battle = WarpBattle(static () => new SkecsStore(), WarpConfig());
        var initial = WarpFrame(battle);
        var victim = initial.Ships.First(s => s.Team == 0 && s.Kind == (int)ShipClass.Interceptor);
        Require(battle.TryFindShip(victim.Serial, out var e), "warp production victim lookup");
        battle.Damage(e, 1000000, false, false);
        WarpStep(battle);
        var home = WarpFrame(battle).Ships.First(s => s.Team == 0 && ShipCatalog.IsCarrier((ShipClass)s.Kind));
        Require(battle.TryFindShip(home.Serial, out var carrier), "warp production carrier lookup");
        battle.Store.Get<Carrier>(carrier).NextBuildTick = battle.Tick + 1;
        WarpStep(battle);
        Require(battle.Counters.Builds == 1 && battle.Counters.ReinforcementShips == 0 &&
            WarpFrame(battle).Ships.Count(s => s.Team == 0) == 10, "warp retains normal carrier production");
    }

    private static void WarpLegacyRespawnCheck()
    {
        using var battle = WarpBattle(static () => new SkecsStore(), WarpConfig() with { EndlessReinforcements = false });
        var carrier = WarpFrame(battle).Ships.First(s => s.Team == 0 && ShipCatalog.IsCarrier((ShipClass)s.Kind));
        Require(battle.TryFindShip(carrier.Serial, out var e), "warp legacy carrier lookup");
        battle.Damage(e, 1000000, false, false);
        WarpStep(battle);
        WarpUntil(battle, 1800);
        Require(WarpFrame(battle).Ships.Count(s => s.Team == 0) == 9, "warp off retains legacy carrier respawn delay");
        var frame = WarpStep(battle);
        Require(frame.Ships.Count(s => s.Team == 0) == 10 &&
            frame.Ships.Any(s => s.Team == 0 && s.Kind == carrier.Kind && s.Serial > carrier.Serial) &&
            battle.Counters.ReinforcementWaves == 0 && battle.Counters.ReinforcementShips == 0,
            "warp off preserves legacy per-carrier revival without warp telemetry");
        battle.Validate();
    }

    private static void WarpBlockedArrivalCheck()
    {
        using var battle = WarpBattle(static () => new SkecsStore(), WarpConfig() with
        {
            ShipsPerTeam = 32, ReinforcementCheckIntervalTicks = 1, ReinforcementWarmupTicks = 2,
            ReinforcementSpawnIntervalTicks = 3, ReinforcementCooldownTicks = 60
        });
        WarpDeplete(battle, 0);
        var wave = battle.Reinforcement(0);
        Require(wave.Active && wave.ArrivalTick == battle.Tick + 2,
            "warp configurable check interval and warmup govern scheduling");
        var blockers = WarpFrame(battle).Ships.Where(s => s.Team == 1).Take(32).Select(s =>
        {
            Require(battle.TryFindShip(s.Serial, out var e), "warp physical blocker lookup");
            return e;
        }).ToArray();
        while (battle.Tick < wave.EndTick)
        {
            for (var slot = 0; slot < blockers.Length; slot++)
            {
                var angle = slot < 8 ? slot * MathF.Tau / 8 : (slot - 8) * MathF.Tau / 24;
                var distance = slot == 0 ? 0 : slot < 8 ? 16 : 28;
                var position = wave.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
                battle.Store.Get<Transform>(blockers[slot]) = new Transform { Position = position, Previous = position };
            }
            var attempts = battle.Counters.ReinforcementPlacementChecks;
            WarpStep(battle);
            Require(battle.Counters.ReinforcementPlacementChecks - attempts <= 32,
                "warp blocked spawn uses at most 32 broadphase placement attempts per tick");
            Require(battle.Counters.ReinforcementShips == 0, "warp physically blocked arrivals defer without spawning");
            var pending = battle.Reinforcement(0);
            if (pending.Active && battle.Tick >= wave.ArrivalTick)
                Require(pending.NextSpawnTick > battle.Tick && pending.NextSpawnTick <= battle.Tick + 3,
                    "warp blocked placement postpones by configured spawn interval");
        }
        Require(!battle.Reinforcement(0).Active && battle.Counters.ReinforcementWaves == 1 &&
            battle.Counters.ReinforcementPlacementChecks > 0, "warp blocked wave closes at bounded deadline");
        WarpUntil(battle, battle.Tick + 59);
        Require(battle.Counters.ReinforcementWaves == 1, "warp deadline close honors configured cooldown");
        WarpStep(battle);
        Require(battle.Counters.ReinforcementWaves == 2, "warp retries depleted fleet after cooldown");
        WarpUntil(battle, battle.Tick + 2);
        Require(battle.Counters.ReinforcementShips == 1, "warp unblocked next wave arrives after configured warmup");
        battle.Validate();
    }

    private static ulong[] WarpRepeatedWaves<TStore>(Func<TStore> factory, int particles) where TStore : IEcsStore
    {
        using var battle = WarpBattle(factory, WarpConfig(particles));
        var hashes = new List<ulong>();
        var corners = new[] { new HashSet<int>(), new HashSet<int>() };
        var escortKinds = new HashSet<int>();
        string? previousComposition = null;
        for (var wave = 0; wave < 4; wave++)
        {
            var survivors = WarpDeplete(battle, 0, 1);
            var known = WarpFrame(battle).Ships.Select(s => s.Serial).ToHashSet();
            var wavesBefore = battle.Counters.ReinforcementWaves;
            var shipsBefore = battle.Counters.ReinforcementShips;
            var deadline = battle.Tick + 360;
            while (battle.Counters.ReinforcementWaves == wavesBefore && battle.Tick < deadline) WarpStep(battle);
            Require(battle.Counters.ReinforcementWaves == wavesBefore + 2, "warp one pending wave per depleted team");
            var arrivals = new List<RenderEntity>();
            var end = battle.Tick + battle.Config.ReinforcementWarmupTicks + 12 * battle.Config.ReinforcementSpawnIntervalTicks;
            while (battle.Tick < end)
            {
                var frame = WarpStep(battle);
                arrivals.AddRange(frame.Ships.Where(s => known.Add(s.Serial)));
            }
            Require(arrivals.Count > 0 && battle.Counters.ReinforcementShips - shipsBefore == arrivals.Count,
                "warp repeated-wave ship counters equal actual arrivals");
            Require(battle.Counters.ReinforcementWaves == wavesBefore + 2, "warp cooldown prevents duplicate waves");
            for (var team = 0; team < 2; team++)
            {
                var teamArrivals = arrivals.Where(s => s.Team == team).ToArray();
                Require(teamArrivals.Any(s => ShipCatalog.IsCarrier((ShipClass)s.Kind)),
                    "warp wave restores at least one absent carrier");
                Require(teamArrivals.Any(s => s.Kind < (int)ShipClass.Corvette) &&
                    teamArrivals.Where(s => s.Kind < (int)ShipClass.LightCarrier).Select(s => s.Kind).Distinct().Count() > 1,
                    "warp wave includes varied escorts");
                foreach (var ship in teamArrivals.Where(s => s.Kind < (int)ShipClass.LightCarrier)) escortKinds.Add(ship.Kind);
                var waveCorners = teamArrivals.Select(WarpCorner).Distinct().ToArray();
                Require(waveCorners.Length == 1, "warp each team wave arrives from one corner");
                var status = battle.Reinforcement(team);
                var expectedCorner = ((int)((battle.Config.Seed ^ (uint)(team * 7919)) & 3) + wave) % 4;
                Require(status.Wave == wave + 1 && status.Corner == expectedCorner && !status.Active,
                    "warp completed wave corner derives deterministically from seed, team and wave number");
                Require(waveCorners.Single() == expectedCorner, "warp arrivals match the scheduled team corner");
                corners[team].Add(waveCorners.Single());
            }
            var composition = string.Join(",", arrivals.Where(s => s.Team == 0).Select(s => s.Kind).Order());
            Require(previousComposition == null || composition != previousComposition,
                "warp successive waves vary composition");
            previousComposition = composition;
            Require(survivors.All(id => WarpFrame(battle).Ships.Any(s => s.Serial == id)), "warp repeated waves keep survivors");
            battle.Validate();
            hashes.Add(battle.Checksum());
        }
        Require(corners.All(c => c.Count == 4), "warp corner rotation visits all four corners for both teams");
        Require(escortKinds.Any(k => k < (int)ShipClass.Corvette) &&
            escortKinds.Any(k => k is (int)ShipClass.Corvette or (int)ShipClass.Frigate or (int)ShipClass.Destroyer),
            "warp composition cycle includes both small and heavy escort classes");
        var count = battle.ShipCount;
        var waves = battle.Counters.ReinforcementWaves;
        var ships = battle.Counters.ReinforcementShips;
        WarpUntil(battle, battle.Tick + 1900);
        Require(battle.ShipCount == count && battle.Counters.ReinforcementWaves == waves &&
            battle.Counters.ReinforcementShips == ships && battle.Counters.Builds == 0,
            "warp repeated waves do not retain legacy per-destroyed-carrier respawns");
        battle.Validate();
        hashes.Add(battle.Checksum());
        return hashes.ToArray();
    }

    private static int WarpCorner(RenderEntity ship)
    {
        Require(MathF.Abs(ship.Position.X) > 80 && MathF.Abs(ship.Position.Y) > 80,
            "warp arrival is in an arena corner, not at legacy carrier spawn");
        return (ship.Position.X > 0 ? 1 : 0) | (ship.Position.Y > 0 ? 2 : 0);
    }

    private static void WarpParticleBudgetCheck()
    {
        using var battle = WarpBattle(static () => new SkecsStore(), WarpConfig(8) with { MaxParticles = 3 });
        WarpDeplete(battle, 0, 1);
        WarpUntil(battle, 210);
        Require(battle.Counters.ReinforcementShips > 0, "warp exhausted tiny effect budget cannot suppress ships");
        battle.Validate();
    }
}
