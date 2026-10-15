using System.Numerics;
using SpaceBattle;

internal static class FleetDeploymentValidation
{
    private static readonly FleetComposition[] Compositions = [FleetComposition.Uniform, FleetComposition.LightSwarm];
    private static readonly int[] Checkpoints = [0, 120, 360, 720];

    public static void Run()
    {
        Require(new BattleConfig().Deployment == FleetDeployment.LegacyRandom, "core deployment remains opt-in");
        Require(FleetDeploymentRules.Revision == "dispersed-front-v2", "deployment revision");
        Require(FleetDeploymentRules.EscortClearance == 2.4f && FleetDeploymentRules.CarrierClearance == 4,
            "deployment surface clearance contract");
        PlannerChecks();
        var expected = PortChecks(static () => new SkecsStore());
        Check(PortChecks(static () => new LeoEcsLiteStore()), "Lite");
        Check(PortChecks(static () => new LeoEcsClassicStore()), "Classic");
        Check(PortChecks(static () => new MorpehStore()), "Morpeh");
        Check(PortChecks(static () => new StaticEcsStore()), "Static");
        Console.WriteLine("PASS: tactical deployment geometry, reflection, fleet attributes, exact legacy positions and repeated 0/120/360/720 checkpoints across all five ports.");

        void Check(ulong[] actual, string name) => Require(expected.SequenceEqual(actual),
            $"{name}: tactical replay matches SKECS at every checkpoint");
    }

    private static BattleConfig Config(int count, int carriers, FleetComposition composition, uint seed = 27) => new()
    {
        ShipsPerTeam = count, CarriersPerTeam = carriers, FleetComposition = composition,
        Deployment = FleetDeployment.Tactical, Seed = seed, ParticleMultiplier = 0, ReplenishCarriers = false
    };

    private static void PlannerChecks()
    {
        foreach (var composition in Compositions)
        foreach (var count in new[] { 8, 16, 96, 160 })
        foreach (var carriers in new[] { 2, 3 })
        foreach (var seed in new uint[] { 0, 27, 7331 })
        {
            var config = Config(count, carriers, composition, seed);
            var plan = FleetDeploymentRules.Create(config);
            Require(plan.Carriers.Length == carriers && plan.Escorts.Length == count, "planner preserves requested counts");
            var repeat = FleetDeploymentRules.Create(config);
            Require(plan.Carriers.SequenceEqual(repeat.Carriers) && plan.Escorts.SequenceEqual(repeat.Escorts),
                $"{composition}/{count}/{carriers}/{seed}: exact deterministic planner positions");
            var circles = new List<(Vector2 Position, float Radius)>();
            for (var i = 0; i < carriers; i++)
                circles.Add((plan.Carriers[i], ShipCatalog.Radius(ShipClass.LightCarrier + i)));
            for (var i = 0; i < count; i++)
                circles.Add((plan.Escorts[i], ShipCatalog.Radius(FleetCompositionRules.Escort(composition, i))));
            Require(circles.All(c => c.Position.X > c.Radius), "positive-X plan leaves a central standoff");
            var bothTeams = circles.Concat(circles.Select(c => (new Vector2(-c.Position.X, c.Position.Y), c.Radius))).ToArray();
            Geometry(bothTeams, config.ArenaHalfSize, $"planner {composition}/{count}/{carriers}/{seed}");
            PlanClearance(plan, config);
        }

        foreach (var composition in Compositions)
        foreach (var carriers in new[] { 2, 3 })
        {
            var config = Config(96, carriers, composition);
            var baseline = FleetDeploymentRules.Create(config);
            foreach (var seed in new uint[] { 0, 1, 7331, uint.MaxValue })
            {
                var seededConfig = config with { Seed = seed };
                var plan = FleetDeploymentRules.Create(seededConfig);
                var repeat = FleetDeploymentRules.Create(seededConfig);
                Require(plan.Carriers.SequenceEqual(repeat.Carriers) && plan.Escorts.SequenceEqual(repeat.Escorts),
                    "irregular seeded geometry repeats exactly");
                Require(!baseline.Escorts.SequenceEqual(plan.Escorts), "seed changes actual escort positions");
                Require(!baseline.Carriers.Select(p => p.X).SequenceEqual(plan.Carriers.Select(p => p.X)) &&
                    !baseline.Carriers.Select(p => p.Y).SequenceEqual(plan.Carriers.Select(p => p.Y)),
                    "seed changes carrier X and Y positions");
                IrregularPositions(plan, seededConfig);
                PlanClearance(plan, seededConfig);
            }
        }

        foreach (var composition in Compositions)
            Reject(() => FleetDeploymentRules.Create(Config(10000, 3, composition) with { ArenaHalfSize = 80 }),
                "unplaceable dense fleet");
        foreach (var arena in new[] { 80f, 90f, 160f, 600f, float.MaxValue })
        foreach (var composition in Compositions)
        foreach (var carriers in new[] { 2, 3 })
        foreach (var seed in new uint[] { 0, 27, 7331, uint.MaxValue })
        {
            var config = Config(16, carriers, composition, seed) with { ArenaHalfSize = arena };
            var plan = FleetDeploymentRules.Create(config);
            var repeat = FleetDeploymentRules.Create(config);
            Require(plan.Carriers.SequenceEqual(repeat.Carriers) && plan.Escorts.SequenceEqual(repeat.Escorts),
                "small/extreme arena seeded planner repeats exactly");
            var circles = plan.Carriers.Select((p, i) => (p, ShipCatalog.Radius(ShipClass.LightCarrier + i)))
                .Concat(plan.Escorts.Select((p, i) => (p, ShipCatalog.Radius(FleetCompositionRules.Escort(composition, i))))).ToArray();
            Geometry(circles.Concat(circles.Select(c => (new Vector2(-c.p.X, c.p.Y), c.Item2))).ToArray(),
                arena, $"arena boundary/cap {arena}/{composition}/{carriers}/{seed}");
            PlanClearance(plan, config);
            Require(circles.All(c => c.p.X <= 600 && MathF.Abs(c.p.Y) <= 600), "bounded planning extent even at extreme arena size");
        }
        foreach (var arena in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        foreach (var mode in new[] { FleetDeployment.LegacyRandom, FleetDeployment.Tactical })
        {
            var invalid = Config(8, 3, FleetComposition.Uniform) with { ArenaHalfSize = arena, Deployment = mode };
            Reject(invalid.Validate, "nonfinite arena config");
            Reject(() => FleetDeploymentRules.Create(invalid), "nonfinite arena planner");
        }
        foreach (var mode in new[] { (FleetDeployment)(-1), (FleetDeployment)2, (FleetDeployment)int.MaxValue })
        {
            var invalid = Config(8, 3, FleetComposition.Uniform) with { Deployment = mode };
            Reject(invalid.Validate, "unknown deployment config");
            Reject(() => FleetDeploymentRules.Create(invalid), "unknown deployment planner");
        }
    }

    private static void IrregularPositions(FleetDeploymentPlan plan, BattleConfig config)
    {
        var front = MathF.Max(16, MathF.Min(config.ArenaHalfSize, 600) * 0.28f);
        var spacing = FleetDeploymentRules.SlotSpacing;
        static bool OffGrid(float value, float origin, float step) =>
            MathF.Abs((value - origin) / step - MathF.Round((value - origin) / step)) > 0.001f;
        Require(plan.Escorts.Count(p => OffGrid(p.X, front, spacing) && OffGrid(p.Y, 0, spacing)) >= plan.Escorts.Length / 4,
            "actual escorts leave the hidden fallback lattice on both axes");
        // Centimeter buckets ignore float noise; a common-offset grid must not pass.
        Require(plan.Escorts.Select(p => MathF.Round(p.X * 100)).Distinct().Count() > plan.Escorts.Length / 2 &&
            plan.Escorts.Select(p => MathF.Round(p.Y * 100)).Distinct().Count() > plan.Escorts.Length / 2,
            "escort positions do not collapse into shared columns or rows");
        Require(plan.Carriers.Select(p => MathF.Round(p.X * 100)).Distinct().Count() == plan.Carriers.Length,
            "carriers have nonaligned depths");
    }

    private static void PlanClearance(FleetDeploymentPlan plan, BattleConfig config)
    {
        var circles = plan.Carriers.Select((p, i) => (Position: p, Radius: ShipCatalog.Radius(ShipClass.LightCarrier + i)))
            .Concat(plan.Escorts.Select((p, i) => (Position: p, Radius: ShipCatalog.Radius(FleetCompositionRules.Escort(config.FleetComposition, i)))))
            .ToArray();
        Geometry(circles.Concat(circles.Select(c => (new Vector2(-c.Position.X, c.Position.Y), c.Radius))).ToArray(),
            config.ArenaHalfSize, $"seeded clearance {config.FleetComposition}/{config.Seed}");
        for (var i = 0; i < circles.Length; i++)
        for (var j = 0; j < i; j++)
        {
            var a = circles[i]; var b = circles[j];
            var dx = (double)a.Position.X - b.Position.X; var dy = (double)a.Position.Y - b.Position.Y;
            var gap = Math.Sqrt(dx * dx + dy * dy) - a.Radius - b.Radius;
            var required = j < plan.Carriers.Length ? 4.0 : 2.4;
            Require(gap >= required - 0.0001,
                $"{config.FleetComposition}/{config.ArenaHalfSize}/{config.Seed}: hulls {j}/{i} preserve surface gap {required}");
        }
    }

    private static ulong[] PortChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        foreach (var composition in Compositions)
        foreach (var count in new[] { 8, 16, 96, 160 })
        foreach (var carriers in new[] { 2, 3 })
        {
            var config = Config(count, carriers, composition);
            BattleFrame frame;
            InitialSnapshot attributes;
            using (var tactical = new Battle<TStore>(factory(), config))
            {
                frame = Frame(tactical);
                InitialChecks(tactical, frame, config);
                attributes = Snapshot(tactical, frame);
                tactical.Validate();
            }
            // Omitting Deployment here exercises the legacy default, not just its enum branch.
            var legacyConfig = new BattleConfig
            {
                ShipsPerTeam = count, CarriersPerTeam = carriers, FleetComposition = composition,
                Seed = config.Seed, ParticleMultiplier = 0, ReplenishCarriers = false
            };
            using var legacy = new Battle<TStore>(factory(), legacyConfig);
            var old = Frame(legacy);
            LegacyPositions(old, legacyConfig);
            Attributes(attributes, Snapshot(legacy, old));
            Require(!frame.Ships.Select(s => s.Position).SequenceEqual(old.Ships.Select(s => s.Position)),
                "tactical positions intentionally differ from legacy");
            legacy.Validate();
        }
        // Seed zero has special RandomStream semantics and must retain the original stream ordering.
        using (var zero = new Battle<TStore>(factory(), new BattleConfig
        {
            ShipsPerTeam = 16, CarriersPerTeam = 2, Seed = 0, ParticleMultiplier = 0, ReplenishCarriers = false
        })) LegacyPositions(Frame(zero), zero.Config);
        using (var explicitLegacy = new Battle<TStore>(factory(), new BattleConfig
        {
            ShipsPerTeam = 16, Deployment = FleetDeployment.LegacyRandom, ParticleMultiplier = 0, ReplenishCarriers = false
        })) LegacyPositions(Frame(explicitLegacy), explicitLegacy.Config);

        var hashes = new List<ulong>();
        foreach (var composition in Compositions)
        {
            var config = Config(96, 3, composition);
            var first = Replay(factory, config);
            var second = Replay(factory, config);
            Require(first.SequenceEqual(second), $"{typeof(TStore).Name}/{composition}: two exact tactical replays");
            hashes.AddRange(first);
        }
        return hashes.ToArray();
    }

    private static ulong[] Replay<TStore>(Func<TStore> factory, BattleConfig config) where TStore : IEcsStore
    {
        using var battle = new Battle<TStore>(factory(), config);
        var result = new List<ulong>();
        foreach (var tick in Checkpoints)
        {
            while (battle.Tick < tick) battle.Step();
            battle.Validate();
            Require(battle.Counters.SelfHits == 0, $"{battle.Framework}/{config.FleetComposition}/tick {tick}: no self-hits");
            result.Add(battle.Checksum());
        }
        return result.ToArray();
    }

    private static void InitialChecks<TStore>(Battle<TStore> battle, BattleFrame frame, BattleConfig config)
        where TStore : IEcsStore
    {
        var plan = FleetDeploymentRules.Create(config);
        Require(frame.Tick == 0 && frame.Ships.Count == 2 * (config.ShipsPerTeam + config.CarriersPerTeam),
            "initial visible ship population");
        var teams = Enumerable.Range(0, 2).Select(team => frame.Ships.Where(s => s.Team == team)
            .OrderBy(s => s.Serial).ToArray()).ToArray();
        for (var team = 0; team < 2; team++)
        {
            Require(battle.TeamShipCount(team) == teams[team].Length, "team population accounting");
            var homes = teams[team].Take(config.CarriersPerTeam).ToArray();
            for (var i = 0; i < teams[team].Length; i++)
            {
                var ship = teams[team][i];
                var isCarrier = i < config.CarriersPerTeam;
                var ordinal = i - config.CarriersPerTeam;
                var kind = isCarrier ? ShipClass.LightCarrier + i : FleetCompositionRules.Escort(config.FleetComposition, ordinal);
                var position = isCarrier ? plan.Carriers[i] : plan.Escorts[ordinal];
                if (team == 0) position.X = -position.X;
                Require(ship.Kind == (int)kind && ship.Radius == ShipCatalog.Radius(kind), "carrier/escort class order and radii");
                Require(ship.Position == position, "battle applies public plan with team-X reflection only");
                var entity = Ship(battle, ship.Serial);
                var home = battle.Store.Get<Brain>(entity).Home;
                Require(isCarrier ? home.IsNone : home == Ship(battle, homes[ordinal % homes.Length].Serial),
                    "round-robin carrier home assignment unchanged");
                var transform = battle.Store.Get<Transform>(entity);
                Require(transform.Previous == position && transform.Angle == (team == 0 ? 0 : MathF.PI),
                    "initial previous position and facing");
            }
        }
        for (var i = 0; i < teams[0].Length; i++)
        {
            var left = teams[0][i]; var right = teams[1][i];
            Require(left.Kind == right.Kind && left.Position == new Vector2(-right.Position.X, right.Position.Y),
                "both teams have reflected counts, class order and positions");
        }
        Geometry(frame.Ships.Select(s => (s.Position, s.Radius)).ToArray(), config.ArenaHalfSize, battle.Framework);
        var expectedTurrets = frame.Ships.Sum(s => ShipCatalog.TurretCount((ShipClass)s.Kind));
        Require(frame.Turrets.Count == expectedTurrets && battle.TurretCount == expectedTurrets, "initial turret population");
        foreach (var ship in frame.Ships)
        {
            var guns = frame.Turrets.Where(g => g.ParentSerial == ship.Serial).ToArray();
            Require(guns.Length == ShipCatalog.TurretCount((ShipClass)ship.Kind), "turret parent count per ship");
            Require(guns.All(g => g.Team == ship.Team && Vector2.Distance(g.Position, ship.Position) <= ship.Radius),
                "turrets remain attached to their own team's parent hull");
        }
    }

    private static void LegacyPositions(BattleFrame frame, BattleConfig config)
    {
        var rng = new RandomStream(config.Seed);
        var serial = 0;
        for (var team = 0; team < 2; team++)
        {
            var ships = frame.Ships.Where(s => s.Team == team).OrderBy(s => s.Serial).ToArray();
            Require(ships.Length == config.CarriersPerTeam + config.ShipsPerTeam, "legacy exact team count");
            for (var i = 0; i < ships.Length; i++)
            {
                var carrier = i < config.CarriersPerTeam;
                var kind = carrier ? ShipClass.LightCarrier + i : FleetCompositionRules.Escort(config.FleetComposition, i - config.CarriersPerTeam);
                var expected = carrier
                    ? new Vector2(team == 0 ? -90 : 90, (i - (config.CarriersPerTeam - 1) * 0.5f) * 34)
                    : new Vector2((team == 0 ? -1 : 1) * rng.Range(40, 72), rng.Range(-65, 65));
                Require(ships[i].Position == expected && ships[i].Kind == (int)kind && ships[i].Serial == ++serial,
                    $"legacy exact RNG position/class/serial team {team}, ordinal {i}");
                serial += ShipCatalog.TurretCount(kind);
            }
        }
    }

    private sealed record ShipSnapshot(int Serial, int HomeSerial, int InventoryCapacity, int InventoryNextUseTick,
        InventoryItem[] Items, Dictionary<Type, object> Components);
    private sealed record InitialSnapshot(ShipSnapshot[] Ships, MountSnapshot[] Mounts);

    private static InitialSnapshot Snapshot<TStore>(Battle<TStore> battle, BattleFrame frame)
        where TStore : IEcsStore
    {
        var rows = new List<ShipSnapshot>();
        foreach (var ship in frame.Ships)
        {
            var e = Ship(battle, ship.Serial);
            var components = new Dictionary<Type, object>();
            Add<Identity>(); Add<Motion>(); Add<Hull>(); Add<Shield>(); Add<Heat>(); Add<Weapon>();
            Add<Collider>(); Add<CombatState>(); Add<Carrier>(); Add<SecondaryWeapon>();
            var brain = battle.Store.Get<Brain>(e);
            var home = HomeSerial(battle.Store, brain.Home);
            brain.Home = EntityKey.None;
            components.Add(typeof(Brain), brain);
            var inventory = battle.Store.Get<Inventory>(e);
            var items = new InventoryItem[16];
            for (var i = 0; i < items.Length; i++) items[i] = inventory.Items[i];
            rows.Add(new(ship.Serial, home, inventory.Capacity, inventory.NextUseTick, items, components));

            void Add<T>() where T : unmanaged
            {
                if (battle.Store.Has<T>(e)) components.Add(typeof(T), battle.Store.Get<T>(e));
            }
        }
        return new(rows.OrderBy(r => r.Serial).ToArray(), Mounts(battle.Store));
    }

    private static void Attributes(InitialSnapshot tactical, InitialSnapshot legacy)
    {
        Require(tactical.Ships.Length == legacy.Ships.Length && tactical.Mounts.Length == legacy.Mounts.Length,
            "deployment changes no entity counts");
        for (var i = 0; i < tactical.Ships.Length; i++)
        {
            var ship = tactical.Ships[i]; var old = legacy.Ships[i];
            Require(ship.Serial == old.Serial && ship.HomeSerial == old.HomeSerial, "ship serial and home assignment unchanged");
            Require(ship.InventoryCapacity == old.InventoryCapacity && ship.InventoryNextUseTick == old.InventoryNextUseTick &&
                ship.Items.SequenceEqual(old.Items), "inventory capacity, cooldown and all contents unchanged");
            Require(ship.Components.Count == old.Components.Count, "ship component count unchanged");
            foreach (var component in ship.Components)
                Require(old.Components.TryGetValue(component.Key, out var value) && component.Value.Equals(value),
                    $"ship {ship.Serial}: {component.Key.Name} membership and attributes unchanged");
        }
        Require(tactical.Mounts.SequenceEqual(legacy.Mounts),
            "turret serials, parent serials, offsets, slot order and armament unchanged");
    }

    private readonly record struct MountSnapshot(int Serial, int ParentSerial, int Slot, Vector2 Offset,
        int NextDecisionTick, int LastFireTick, Weapon Weapon, Heat Heat);

    private struct MountCollector : IProcessor<MountedTurret>
    {
        public IEcsStore Store;
        public List<MountSnapshot> Rows;
        public void Execute(EntityKey e, ref MountedTurret mount)
        {
            Require(Store.Alive(mount.Parent) && Store.Has<Identity>(mount.Parent) && mount.Target.IsNone,
                "initial mounted turret has a live ship parent and no target");
            var id = Store.Get<Identity>(mount.Parent);
            Require(mount.Slot >= 0 && mount.Slot < ShipCatalog.TurretCount(id.Class), "mount slot valid for parent class");
            var expected = ShipCatalog.TurretWeapon(id.Class, mount.Slot);
            Require(EqualityComparer<Weapon>.Default.Equals(Store.Get<Weapon>(e), expected), "mounted catalog weapon unchanged");
            var parent = Store.Get<Transform>(mount.Parent); var transform = Store.Get<Transform>(e);
            var c = MathF.Cos(parent.Angle); var s = MathF.Sin(parent.Angle);
            var offset = mount.LocalOffset;
            var position = parent.Position + new Vector2(offset.X * c - offset.Y * s, offset.X * s + offset.Y * c);
            Require(transform.Position == position && transform.Previous == position && transform.Angle == parent.Angle,
                "mounted turret transform uses parent facing and local offset");
            Rows.Add(new(mount.Serial, id.Serial, mount.Slot, offset, mount.NextDecisionTick, mount.LastFireTick,
                Store.Get<Weapon>(e), Store.Get<Heat>(e)));
        }
    }

    private static MountSnapshot[] Mounts(IEcsStore store)
    {
        var collector = new MountCollector { Store = store, Rows = new() };
        store.ForEach<MountedTurret, MountCollector>(ref collector);
        return collector.Rows.OrderBy(m => m.Serial).ToArray();
    }

    private static int HomeSerial(IEcsStore store, EntityKey home) => home.IsNone ? -1 : store.Get<Identity>(home).Serial;

    private static void Geometry((Vector2 Position, float Radius)[] circles, float arena, string label)
    {
        for (var i = 0; i < circles.Length; i++)
        {
            var a = circles[i];
            Require(float.IsFinite(a.Position.X) && float.IsFinite(a.Position.Y), label + ": finite coordinates");
            Require(MathF.Abs(a.Position.X) + a.Radius <= arena && MathF.Abs(a.Position.Y) + a.Radius <= arena,
                label + ": entire ship circle inside arena");
            for (var j = 0; j < i; j++)
            {
                var b = circles[j]; var clearance = a.Radius + b.Radius;
                // Double intermediates avoid accepting overlaps through squared-float rounding.
                var dx = (double)a.Position.X - b.Position.X; var dy = (double)a.Position.Y - b.Position.Y;
                Require(dx * dx + dy * dy >= (double)clearance * clearance,
                    $"{label}: ship circles {j}/{i} do not overlap");
            }
        }
    }

    private static EntityKey Ship<TStore>(Battle<TStore> battle, int serial) where TStore : IEcsStore
    {
        Require(battle.TryFindShip(serial, out var entity), $"ship serial {serial} lookup");
        return entity;
    }

    private static BattleFrame Frame<TStore>(Battle<TStore> battle) where TStore : IEcsStore
    {
        var frame = new BattleFrame(); battle.Capture(frame); return frame;
    }

    private static void Reject(Action action, string label)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Deployment validation failed: did not reject " + label);
    }

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Deployment validation failed: " + label);
    }
}
