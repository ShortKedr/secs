using SpaceBattle;

internal static partial class Validation
{
    private static readonly int[][] FleetWeights =
    [
        [27, 9, 3, 3, 3, 1, 1, 1],
        [24, 9, 6, 3, 3, 1, 1, 1],
        [30, 9, 3, 2, 2, 0, 1, 1],
        [26, 10, 2, 4, 3, 1, 1, 1]
    ];

    private static void FleetCompositionChecks()
    {
        FleetCycleChecks();
        var expected = FleetStoreChecks(static () => new SkecsStore());
        Check(FleetStoreChecks(static () => new SkecsStore()), "SKECS repeat");
        Check(FleetStoreChecks(static () => new LeoEcsLiteStore()), "Lite");
        Check(FleetStoreChecks(static () => new LeoEcsClassicStore()), "Classic");
        Check(FleetStoreChecks(static () => new MorpehStore()), "Morpeh");
        Check(FleetStoreChecks(static () => new StaticEcsStore()), "Static");
        Console.WriteLine("PASS: fleet composition cycles, smooth prefixes, legacy defaults, initial swarms, weighted production and varied warp waves across all five ports.");

        void Check(ulong[] actual, string name) => Require(expected.SequenceEqual(actual),
            $"{name}: fleet composition matches SKECS at all {expected.Length} deterministic checkpoints");
    }

    private static void FleetCycleChecks()
    {
        Require(FleetCompositionRules.Revision == "light-swarm-v1" && FleetCompositionRules.CycleLength == 48,
            "fleet composition revision and cycle length");
        Require(new BattleConfig().FleetComposition == FleetComposition.Uniform,
            "fleet composition remains opt-in in core/headless defaults");
        for (var variant = 0; variant < FleetWeights.Length; variant++)
        {
            var cycle = Enumerable.Range(0, 48)
                .Select(i => FleetCompositionRules.Escort(FleetComposition.LightSwarm, i, variant)).ToArray();
            Require(FleetCounts(cycle).SequenceEqual(FleetWeights[variant]), $"swarm variant {variant}: exact cycle weights");
            var prefix = new int[8];
            for (var i = 0; i < cycle.Length; i++)
            {
                prefix[(int)cycle[i]]++;
                for (var kind = 0; kind < prefix.Length; kind++)
                    Require(Math.Abs(prefix[kind] - (i + 1) * FleetWeights[variant][kind] / 48.0) <= 2,
                        $"swarm variant {variant}: smooth weighted prefix {i + 1}, class {kind}");
            }
            // Query out of order and interleave modes/variants to catch mutable shared cursors.
            for (var i = 143; i >= 0; i--)
            {
                Require(FleetCompositionRules.Escort(FleetComposition.Uniform, i, variant) == (ShipClass)(i % 8),
                    "uniform ordinal modulo eight is unchanged for every variant");
                _ = FleetCompositionRules.Escort(FleetComposition.LightSwarm, i + 17, (variant + 1) % 4);
                Require(FleetCompositionRules.Escort(FleetComposition.LightSwarm, i, variant) == cycle[i % 48],
                    $"swarm variant {variant}: repeatable periodic lookup independent of interleaving");
            }
            Require(FleetCompositionRules.Escort(FleetComposition.LightSwarm, int.MaxValue, variant) == cycle[int.MaxValue % 48],
                "swarm accepts large nonnegative ordinals without overflow");
        }
        Require(FleetCompositionRules.Escort(FleetComposition.LightSwarm, 17) ==
            FleetCompositionRules.Escort(FleetComposition.LightSwarm, 17, 0), "omitted wave variant means zero");
        Require(FleetCompositionRules.Escort(FleetComposition.Uniform, int.MaxValue) == (ShipClass)(int.MaxValue % 8),
            "uniform accepts large nonnegative ordinals");
        new BattleConfig { FleetComposition = FleetComposition.LightSwarm }.Validate();
        foreach (var composition in new[] { FleetComposition.Uniform, FleetComposition.LightSwarm })
        {
            Reject(() => FleetCompositionRules.Escort(composition, -1), "negative escort ordinal");
            Reject(() => FleetCompositionRules.Escort(composition, int.MinValue), "minimum escort ordinal");
            Reject(() => FleetCompositionRules.Escort(composition, 0, -1), "negative wave variant");
            Reject(() => FleetCompositionRules.Escort(composition, 0, 4), "wave variant past final cycle");
            Reject(() => FleetCompositionRules.Escort(composition, 0, int.MaxValue), "large invalid wave variant");
        }
        Reject(() => FleetCompositionRules.Escort((FleetComposition)2, 0), "unknown composition");
        Reject(() => new BattleConfig { FleetComposition = (FleetComposition)2 }.Validate(), "invalid config composition");

        static void Reject(Action action, string name)
        {
            try { action(); }
            catch (ArgumentOutOfRangeException) { return; }
            Require(false, "fleet composition rejects " + name);
        }
    }

    private static int[] FleetCounts(IEnumerable<ShipClass> kinds)
    {
        var counts = new int[8];
        foreach (var kind in kinds)
        {
            Require((int)kind is >= 0 and < 8, "fleet cycle contains escorts only");
            counts[(int)kind]++;
        }
        return counts;
    }

    private static ulong[] FleetStoreChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        var hashes = new List<ulong>();
        foreach (var composition in new[] { FleetComposition.Uniform, FleetComposition.LightSwarm })
        {
            // Uniform deliberately uses the default property rather than explicitly opting in.
            var config = new BattleConfig { ShipsPerTeam = 96, Seed = 27, ParticleMultiplier = 0, ReplenishCarriers = false };
            if (composition == FleetComposition.LightSwarm) config = config with { FleetComposition = composition };
            using var battle = WarpBattle(factory, config);
            var initial = WarpFrame(battle);
            for (var team = 0; team < 2; team++)
            {
                var ships = initial.Ships.Where(s => s.Team == team).OrderBy(s => s.Serial).ToArray();
                var escorts = ships.Where(s => !ShipCatalog.IsCarrier((ShipClass)s.Kind)).ToArray();
                var expectedCounts = composition == FleetComposition.Uniform
                    ? new[] { 12, 12, 12, 12, 12, 12, 12, 12 } : new[] { 54, 18, 6, 6, 6, 2, 2, 2 };
                Require(ships.Length == 99 && FleetCounts(escorts.Select(s => (ShipClass)s.Kind)).SequenceEqual(expectedCounts),
                    $"{battle.Framework}: {composition} exact initial counts for team {team}");
                Require(ships.Where(s => ShipCatalog.IsCarrier((ShipClass)s.Kind)).Select(s => (ShipClass)s.Kind)
                    .SequenceEqual(new[] { ShipClass.LightCarrier, ShipClass.FleetCarrier, ShipClass.FoundryCarrier }),
                    "fleet composition preserves all three initial carriers");
                Require(escorts.Select(s => (ShipClass)s.Kind).SequenceEqual(Enumerable.Range(0, 96)
                    .Select(i => FleetCompositionRules.Escort(composition, i))), "initial escort order follows composition cycle");
            }
            battle.Validate(); hashes.Add(battle.Checksum());
            FleetProductionChecks(battle, composition, hashes);
        }
        FleetWarpChecks(factory, hashes);
        return hashes.ToArray();
    }

    private static void FleetProductionChecks<TStore>(Battle<TStore> battle, FleetComposition composition, List<ulong> hashes)
        where TStore : IEcsStore
    {
        var initial = WarpFrame(battle);
        foreach (var ship in initial.Ships.Where(s => s.Team == 0 && !ShipCatalog.IsCarrier((ShipClass)s.Kind)).Take(48))
        {
            Require(battle.TryFindShip(ship.Serial, out var e), "fleet production victim lookup");
            battle.Damage(e, 1000000, false, false);
        }
        WarpStep(battle);
        var home = initial.Ships.First(s => s.Team == 0 && ShipCatalog.IsCarrier((ShipClass)s.Kind));
        Require(battle.TryFindShip(home.Serial, out var carrier), "fleet production carrier lookup");
        var known = WarpFrame(battle).Ships.Select(s => s.Serial).ToHashSet();
        var produced = new List<ShipClass>();
        for (var ordinal = 0; ordinal < 48; ordinal++)
        {
            Require(battle.Store.Get<Carrier>(carrier).Built == ordinal, "carrier build ordinal advances naturally");
            battle.Store.Get<Carrier>(carrier).NextBuildTick = battle.Tick + 1;
            var frame = WarpStep(battle);
            var arrivals = frame.Ships.Where(s => known.Add(s.Serial)).ToArray();
            Require(arrivals.Length == 1 && arrivals[0].Team == 0 &&
                arrivals[0].Kind == (int)FleetCompositionRules.Escort(composition, ordinal),
                $"{battle.Framework}: {composition} carrier build {ordinal} follows weighted ordinal");
            produced.Add((ShipClass)arrivals[0].Kind);
            Require(battle.Counters.Builds == ordinal + 1 && battle.Store.Get<Carrier>(carrier).Built == ordinal + 1,
                "fleet production counters count actual builds");
            if ((ordinal + 1) % 8 == 0) { battle.Validate(); hashes.Add(battle.Checksum()); }
        }
        var weights = composition == FleetComposition.LightSwarm ? FleetWeights[0] : new[] { 6, 6, 6, 6, 6, 6, 6, 6 };
        Require(FleetCounts(produced).SequenceEqual(weights), "full production cycle has exact composition weights, not modulo eight");
        battle.Store.Get<Carrier>(carrier).NextBuildTick = battle.Tick + 1;
        WarpStep(battle);
        Require(battle.Counters.Builds == 48 && battle.Store.Get<Carrier>(carrier).Built == 48 && battle.TeamShipCount(0) == 99,
            "fleet production retains population cap and does not consume blocked build ordinals");
        battle.Validate(); hashes.Add(battle.Checksum());
    }

    private static void FleetWarpChecks<TStore>(Func<TStore> factory, List<ulong> hashes) where TStore : IEcsStore
    {
        using var battle = WarpBattle(factory, new BattleConfig
        {
            ShipsPerTeam = 96, Seed = 27, FleetComposition = FleetComposition.LightSwarm,
            ParticleMultiplier = 0, EndlessReinforcements = true,
            ReinforcementCheckIntervalTicks = 1, ReinforcementWarmupTicks = 2,
            ReinforcementSpawnIntervalTicks = 1, ReinforcementCooldownTicks = 4
        });
        var variants = new[] { new HashSet<int>(), new HashSet<int>() };
        var mixes = new[] { new HashSet<string>(), new HashSet<string>() };
        for (var waveIndex = 0; waveIndex < 4; waveIndex++)
        {
            var beforeWaves = battle.Counters.ReinforcementWaves;
            var beforeShips = battle.Counters.ReinforcementShips;
            var survivors = WarpDeplete(battle, 0, 1);
            var known = WarpFrame(battle).Ships.Select(s => s.Serial).ToHashSet();
            var deadline = battle.Tick + 16;
            while (battle.Counters.ReinforcementWaves == beforeWaves && battle.Tick < deadline) WarpStep(battle);
            Require(battle.Counters.ReinforcementWaves == beforeWaves + 2, "swarm schedules one wave per depleted team");
            var scheduled = new[] { battle.Reinforcement(0), battle.Reinforcement(1) };
            Require(scheduled.All(w => w.Active && w.PendingShips == 65), "swarm retains ceil(99 * .65) wave size");
            var arrivals = new List<RenderEntity>();
            deadline = scheduled.Max(w => w.EndTick);
            while ((battle.Reinforcement(0).Active || battle.Reinforcement(1).Active) && battle.Tick < deadline)
            {
                var frame = WarpStep(battle);
                arrivals.AddRange(frame.Ships.Where(s => known.Add(s.Serial)));
            }
            Require(!battle.Reinforcement(0).Active && !battle.Reinforcement(1).Active &&
                arrivals.Count == 130 && battle.Counters.ReinforcementShips - beforeShips == arrivals.Count &&
                battle.Counters.ReinforcementWaves == beforeWaves + 2, "swarm waves complete with exact telemetry and no duplicates");
            for (var team = 0; team < 2; team++)
            {
                var status = scheduled[team];
                var ships = arrivals.Where(s => s.Team == team).OrderBy(s => s.Serial).ToArray();
                var escorts = ships.Where(s => !ShipCatalog.IsCarrier((ShipClass)s.Kind)).Select(s => (ShipClass)s.Kind).ToArray();
                var carrierCount = status.Composition == 3 ? 2 : 1;
                Require(ships.Length == 65 && escorts.Length == 65 - carrierCount && battle.TeamShipCount(team) == 66,
                    "swarm retains carrier restoration policy and team population accounting");
                Require(escorts.SequenceEqual(Enumerable.Range(0, escorts.Length)
                    .Select(i => FleetCompositionRules.Escort(FleetComposition.LightSwarm, i, status.Composition))),
                    $"{battle.Framework}: warp variant {status.Composition} uses escort-local weighted ordinals");
                Require(escorts.Count(k => k is ShipClass.Interceptor or ShipClass.Fighter) * 2 > escorts.Length &&
                    escorts.Distinct().Count() >= 6, "swarm warp escorts are predominantly light but varied");
                Require(FleetCounts(escorts.Take(48)).SequenceEqual(FleetWeights[status.Composition]),
                    "warp full escort cycle matches independent variant weights");
                variants[team].Add(status.Composition);
                mixes[team].Add(string.Join(",", FleetCounts(escorts)));
            }
            Require(survivors.All(id => WarpFrame(battle).Ships.Any(s => s.Serial == id)), "swarm warp preserves surviving ships");
            Require(battle.Counters.Builds == 0, "swarm warp fixture excludes carrier production");
            battle.Validate(); hashes.Add(battle.Checksum());
        }
        Require(variants.All(v => v.SetEquals(new[] { 0, 1, 2, 3 })) && mixes.All(m => m.Count == 4),
            "swarm repeated waves visit all four distinct weighted mixes for both teams");
    }
}
