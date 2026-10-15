using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using SpaceBattle;

// Explicit entry point for the main-owned dispatcher; no replay or timing loop here.
internal static partial class AiLocalityValidation
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        Run(static () => new SkecsStore());
        Run(static () => new LeoEcsLiteStore());
        Run(static () => new LeoEcsClassicStore());
        Run(static () => new MorpehStore());
        Run(static () => new StaticEcsStore());
        Console.WriteLine("PASS: AI locality independent readiness/arrival scoring/selection bit parity, lazy gates, serial ties, global search, snapshot rebuilds and copy-free hot-path IL (five adapters).");
    }

    public static void Run<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        FocusedChecks(factory);
        CorpusChecks(factory);
        CopyChecks<TStore>();
    }

    private static void FocusedChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var f = new Fixture<TStore>(factory());
        var actor = f.Spawn(0, ShipClass.Corvette, Vector2.Zero);
        var first = f.Spawn(1, ShipClass.Fighter, new(60, 0));
        var tied = f.Spawn(1, ShipClass.Fighter, new(60, 0));
        f.Spawn(0, ShipClass.Fighter, new(1, 0));
        var docked = f.Spawn(1, ShipClass.Fighter, new(1, 0));
        f.Battle.Store.Set(docked, new Docked { Carrier = EntityKey.None });
        f.Prepare();
        Check(f.Count(0) == -1 && f.Count(1) == -1, "no-decision phase does not prepare enemy lists");
        var oracle = f.Oracle(); var a = f.Index(actor);
        var brain = new Brain { Target = tied, TargetLockUntilTick = 120 };
        CheckSelection(f, oracle, a, null, brain, tied);
        Check(f.Count(0) == -1 && f.Count(1) == -1, "retained lease does not prepare enemy lists");
        CheckSelection(f, oracle, a, null, new Brain { Target = EntityKey.None }, first);
        Check(f.Count(1) == -1, "only the searched team prepares a list");
        CheckIndex(f, 0);
        CheckSelection(f, oracle, a, null, new Brain { Target = tied }, tied);
        var buffer = f.Buffer(0);

        // External ref edits must not affect either the cached or an as-yet-uncached snapshot score.
        f.Prepare(); oracle = f.Oracle(); a = f.Index(actor);
        var oldCost = f.Cost(a, null, f.Index(first));
        var oldTiedCost = oracle.Cost(a, null, f.Index(tied));
        Check(f.Field<bool[]>("targetSpeedReady")[f.Index(first)] &&
            !f.Field<bool[]>("targetSpeedReady")[f.Index(tied)], "one warmed and one cold target invariant");
        ref var weapon = ref f.Battle.Store.Get<Weapon>(actor);
        weapon.Damage *= 7; weapon.Range = 25; weapon.Speed = 1000;
        f.Battle.Store.Get<SecondaryWeapon>(actor).Value.Damage *= 3;
        f.Battle.Store.Get<Heat>(actor).CoolingPerSecond = 0.25f;
        f.Battle.Store.Get<Heat>(actor).Value = 99;
        f.Battle.Store.Get<Motion>(actor).MaxSpeed = 200;
        f.Battle.Store.Get<Identity>(actor).Class = ShipClass.FleetCarrier;
        f.Battle.Store.Get<Hull>(first).Current = 7;
        f.Battle.Store.Get<Hull>(first).Armor = 0.94f;
        f.Battle.Store.Get<Shield>(first).Current = 3;
        f.Battle.Store.Get<Transform>(first).Position = new(600, 0);
        f.Battle.Store.Get<Motion>(first).Velocity = new(2, 0);
        f.Battle.Store.Get<Collider>(first).Radius = 9;
        f.Battle.Store.Get<Brain>(first).Mode = BrainMode.Attack;
        f.Battle.Store.Get<Brain>(first).Target = actor;
        f.Battle.Store.Get<CombatState>(actor).LastAttacker = first;
        f.Battle.Store.Get<CombatState>(actor).LastDamageTick = f.Battle.Tick;
        f.Battle.Store.Get<Identity>(tied).Team = 0;
        Bits(f.Cost(a, null, f.Index(first)), oldCost, "cached score ignores live ref edits");
        Bits(f.Cost(a, null, f.Index(tied)), oldTiedCost, "uncached score ignores live ref edits");
        CheckSelection(f, oracle, a, null, new Brain { Target = EntityKey.None }, first);
        f.Prepare(); oracle = f.Oracle(); a = f.Index(actor);
        Check(f.Count(0) == -1 && f.Count(1) == -1, "new snapshot invalidates enemy lists");
        CheckSelection(f, oracle, a, null, new Brain { Target = EntityKey.None });
        Parity(f.Cost(a, null, f.Index(first)), oracle.Cost(a, null, f.Index(first)), "ref-edited snapshot score");
        CheckIndex(f, 0);
        Check(ReferenceEquals(buffer, f.Buffer(0)), "enemy index reuses capacity on same-size rebuild");

        // Nearer is uncatchable; the reachable candidate lies far outside the spatial formation grid.
        f.Battle.Store.Get<Identity>(actor).Class = ShipClass.Corvette;
        f.Battle.Store.Get<Weapon>(actor) = new Weapon { Kind = WeaponKind.Cannon, Cooldown = 19,
            Damage = 11.3f, Burst = 3, Range = 50, Speed = 1000, HeatPerShot = 1.2f };
        f.Battle.Store.Get<SecondaryWeapon>(actor).Value = default;
        f.Battle.Store.Get<Motion>(first).Velocity = Vector2.Zero;
        f.Battle.Store.Get<Identity>(tied).Team = 1;
        f.Battle.Store.Get<Transform>(tied).Position = new(80, 0);
        f.Battle.Store.Get<Motion>(tied).Velocity = new(1500, 0);
        f.Prepare(); oracle = f.Oracle(); a = f.Index(actor);
        Check(float.IsPositiveInfinity(f.Cost(a, null, f.Index(tied))), "escaping current target is unreachable");
        CheckSelection(f, oracle, a, null, new Brain { Target = tied, TargetLockUntilTick = 120 }, first);

        // Docking and a direct team edit invalidate the old candidates only at the next snapshot.
        f.Battle.Store.Set(first, new Docked { Carrier = EntityKey.None });
        f.Battle.Store.Get<Identity>(tied).Team = 0;
        CheckSelection(f, oracle, a, null, new Brain { Target = EntityKey.None }, first);
        f.Prepare(); oracle = f.Oracle(); a = f.Index(actor);
        CheckSelection(f, oracle, a, null, new Brain { Target = first, TargetLockUntilTick = 120 }, EntityKey.None);
        Check(f.Count(0) == 0, "no enemies yields an empty reusable index");
        Check(!f.Field<bool[]>("targetSpeedReady").Any(v => v), "empty search does not evaluate target speed");

        f.Battle.Store.Remove<Docked>(first);
        f.Prepare(); oracle = f.Oracle(); a = f.Index(actor);
        CheckSelection(f, oracle, a, null, new Brain { Target = EntityKey.None }, first);
        CheckIndex(f, 0);
        f.Battle.Damage(first, 100000, false, false);
        f.Prepare(); oracle = f.Oracle(); a = f.Index(actor);
        CheckSelection(f, oracle, a, null, new Brain { Target = first, TargetLockUntilTick = 120 }, EntityKey.None);

        // Retire every remaining hull without advancing the simulation.
        foreach (var row in f.Rows.ToArray()) f.Battle.Damage(row.Entity, 100000, false, false);
        f.Prepare();
        Check(f.Rows.Count == 0, "empty alive snapshot");
        Check(f.Count(0) == -1 && f.Count(1) == -1, "empty phase needs no target preparation");
    }

    private static void CorpusChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var f = new Fixture<TStore>(factory());
        var random = new Random(61012); var actors = new List<EntityKey>();
        for (var i = 0; i < 22; i++)
        {
            var e = f.Spawn(i % 2, (ShipClass)(i % 11), new(random.Next(-250, 251), random.Next(-250, 251)));
            actors.Add(e);
            f.Battle.Store.Get<Motion>(e).Velocity = new(random.Next(-60, 61), random.Next(-60, 61));
            f.Battle.Store.Get<Motion>(e).MaxSpeed = i % 5 == 0 ? 0.5f : 80;
            f.Battle.Store.Get<Heat>(e) = new Heat { Value = i % 3 * 49, Limit = 100, CoolingPerSecond = i % 4 };
            f.Battle.Store.Get<Hull>(e).Armor = i % 3 == 0 ? 0.99f : 0.23f;
            if (i % 4 == 0) f.Battle.Store.Set(e, new SecondaryWeapon { Value = ShipCatalog.Primary(ShipClass.Gunship) });
            if (i % 7 == 0) f.Battle.Store.Get<Weapon>(e).Cooldown = 0;
            if (i % 7 == 1) f.Battle.Store.Get<Weapon>(e).Damage = 0;
            if (i % 7 == 2) f.Battle.Store.Get<Weapon>(e).Speed = 0;
            if (i % 7 == 3) f.Battle.Store.Get<Weapon>(e).Burst = 0;
            if (i % 7 == 4) f.Battle.Store.Get<Weapon>(e).Range = -0f;
            f.Battle.Store.Get<Brain>(e).Mode = i % 5 == 0 ? BrainMode.Screen : BrainMode.Attack;
        }
        for (var i = 0; i < actors.Count; i++)
        {
            f.Battle.Store.Get<Brain>(actors[i]).Target = actors[(i + 1) % actors.Count];
            f.Battle.Store.Get<CombatState>(actors[i]) = new CombatState
                { LastAttacker = actors[(i + 3) % actors.Count], LastDamageTick = i % 2 == 0 ? -180 : -181 };
        }
        f.Prepare();
        var oracle = f.Oracle();
        var indices = actors.Select(f.Index).ToArray();
        var squad = new Squad { Count = 4, Leader = indices[0] };
        for (var s = 0; s < squad.Count; s++) squad.Members[s] = indices[s];
        for (var pass = 0; pass < 2; pass++)
            foreach (var a in indices)
            {
                // Reverse traversal on the warm pass catches order-dependent cache slots.
                foreach (var t in pass == 0 ? indices : indices.Reverse())
                {
                    Parity(f.Cost(a, null, t), oracle.Cost(a, null, t), "frozen solo cost");
                    Parity(f.Cost(a, squad, t), oracle.Cost(a, squad, t), "frozen squad accumulation order");
                }
                foreach (var until in new[] { 0, 120 })
                {
                    var brain = f.Rows[a].Brain; brain.TargetLockUntilTick = until;
                    CheckSelection(f, oracle, a, null, brain);
                    CheckSelection(f, oracle, a, squad, brain);
                }
            }
        CheckIndex(f, 0); CheckIndex(f, 1);

        // Synthetic nonstandard snapshot team exercises the bounded fallback without changing collector contracts.
        f.Prepare();
        var fallback = f.Index(actors[0]);
        var identity = f.Rows[fallback].Id; identity.Team = -7;
        f.Rows[fallback] = f.Rows[fallback] with { Id = identity };
        oracle = f.Oracle();
        CheckSelection(f, oracle, fallback, null, new Brain { Target = EntityKey.None });
        Check(f.Count(0) == -1 && f.Count(1) == -1, "nonstandard team uses original serial full scan");

        // Gate checks with a fresh phase: unreachable approach must not initialize scoring/speed.
        var actor = actors[0]; var target = actors[1];
        f.Battle.Store.Get<Transform>(actor).Position = Vector2.Zero;
        f.Battle.Store.Get<Transform>(target).Position = new(1000, 0);
        f.Battle.Store.Get<Motion>(actor).MaxSpeed = 0.5f;
        f.Battle.Store.Get<Motion>(target).Velocity = Vector2.Zero;
        f.Prepare();
        var ai = f.Index(actor); var ti = f.Index(target);
        Check(float.IsPositiveInfinity(f.Cost(ai, null, ti)), "approach gate returns infinity");
        Check(!f.Field<ActorScoring[]>("actorScoring")[ai].Ready &&
            !f.Field<ActorScoring[]>("actorScoring")[ai].PrimaryDps.Ready &&
            !f.Field<bool[]>("targetSpeedReady")[ti],
            "approach gate leaves lazy invariants unevaluated");
        f.Battle.Store.Get<Transform>(target).Position = new(20, 0);
        f.Battle.Store.Get<Weapon>(actor) = default;
        f.Battle.Store.Get<SecondaryWeapon>(actor).Value = default;
        f.Prepare(); ai = f.Index(actor); ti = f.Index(target);
        Check(float.IsPositiveInfinity(f.Cost(ai, null, ti)), "invalid weapons yield no contribution");
        Check(!f.Field<ActorScoring[]>("actorScoring")[ai].PrimaryDps.Ready &&
            !f.Field<bool[]>("targetSpeedReady")[ti],
            "weapon gate leaves DPS and speed unevaluated");
    }

    private static void CheckIndex<TStore>(Fixture<TStore> f, int team) where TStore : IEcsStore
    {
        var expected = Enumerable.Range(0, f.Rows.Count).Where(i => !f.Rows[i].Docked && f.Rows[i].Id.Team != team);
        Check(f.Count(team) >= 0 && f.Buffer(team).Take(f.Count(team)).SequenceEqual(expected),
            "enemy list skips only docked/same-team and retains snapshot serial order");
    }

    private static void CheckSelection<TStore>(Fixture<TStore> f, ScoringOracle oracle, int actor,
        Squad? squad, Brain brain, EntityKey? expected = null) where TStore : IEcsStore
    {
        var before = f.Battle.Tactics.TargetRetentions;
        var reference = oracle.Select(actor, squad, brain, out var referenceCost, out var retained);
        var actual = f.Select(actor, squad, brain, out var cost);
        Check(actual == reference && (!expected.HasValue || actual == expected.Value), "frozen selection and target tie");
        Parity(cost, referenceCost, "independent selected cost");
        Check(f.Battle.Tactics.TargetRetentions - before == (retained ? 1 : 0), "retention telemetry");
    }

    private static void Parity(float actual, float expected, string message) => Check(actual == expected ||
        float.IsFinite(actual) && float.IsFinite(expected) && MathF.Abs(actual - expected) <= MathF.Max(1e-5f, MathF.Abs(expected) * 1e-6f), message);

    private static void Bits(float actual, float expected, string message) =>
        Check(BitConverter.SingleToInt32Bits(actual) == BitConverter.SingleToInt32Bits(expected), message);
    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException("AI locality: " + message);
    }

    private sealed class Fixture<TStore> : IDisposable where TStore : IEcsStore
    {
        private static readonly Type Type = typeof(Battle<TStore>);
        public readonly Battle<TStore> Battle;
        public List<AiShipSnapshot> Rows => Field<List<AiShipSnapshot>>("aiRows");
        public Fixture(TStore store, BattleConfig? config = null, bool completeSelection = true)
        {
            var settings = config ?? new BattleConfig { ShipsPerTeam = 8, Seed = 61012, ChurnMultiplier = 1.37f };
            Battle = new(store, completeSelection ? settings with { Tactics = settings.Tactics with { LocalTargetCandidates = 64,
                GlobalTargetProbes = 64, MaximumSpatialWorkPerTick = 1048576, MaximumScoreEvaluationsPerTick = 1048576 } } : settings);
            var frame = new BattleFrame(); Battle.Capture(frame);
            foreach (var ship in frame.Ships)
            {
                Check(Battle.TryFindShip(ship.Serial, out var e), "fixture lookup");
                Battle.Store.Get<Brain>(e).NextDecisionTick = int.MaxValue;
                Battle.Store.Set(e, new Docked { Carrier = EntityKey.None });
            }
        }
        public EntityKey Spawn(int team, ShipClass kind, Vector2 position)
        {
            var e = Battle.SpawnShip(team, kind, position, EntityKey.None);
            Battle.Store.Get<Brain>(e) = new Brain { NextDecisionTick = int.MaxValue, Mode = BrainMode.Patrol,
                Target = EntityKey.None, SquadLeader = EntityKey.None, Home = EntityKey.None, CoverAlly = EntityKey.None };
            Battle.Store.Get<Weapon>(e).NextFireTick = 0;
            Battle.Store.Get<Motion>(e) = new Motion { MaxSpeed = 45 };
            Battle.Store.Get<Hull>(e) = new Hull { Current = 100, Maximum = 100, Armor = 0.17f };
            Battle.Store.Get<Shield>(e) = new Shield { Current = 50, Maximum = 50 };
            Battle.Store.Get<Heat>(e) = new Heat { Limit = 100, CoolingPerSecond = 7.1f };
            return e;
        }
        public void Prepare()
        {
            Battle.Store.Commit(); Battle.Capture(new BattleFrame());
            Type.GetMethod("PrepareTactics", Hidden)!.Invoke(Battle, null);
        }
        public T Field<T>(string name) => (T)Type.GetField(name, Hidden)!.GetValue(Battle)!;
        public int Count(int team) => Field<int>(team == 0 ? "enemyCount0" : "enemyCount1");
        public int[] Buffer(int team) => Field<int[]>(team == 0 ? "enemyTargets0" : "enemyTargets1");
        public int Index(EntityKey entity) => Rows.FindIndex(row => row.Entity == entity);
        public ScoringOracle Oracle() => new(Rows.ToArray(), Battle.Config, Battle.Tick,
            Field<float[]>("committedShield").ToArray(), Field<float[]>("committedHull").ToArray());
        public float Cost(int actor, Squad? squad, int target) =>
            (float)Type.GetMethod("TargetCost", Hidden)!.Invoke(Battle, [actor, squad, target])!;
        public TacticalEstimate Estimate(int actor, Squad? squad, int target) =>
            (TacticalEstimate)Type.GetMethod("EstimateTarget", Hidden)!.Invoke(Battle, [actor, squad, target])!;
        public float Engage(int actor, int target) =>
            (float)Type.GetMethod("MemberEngageSeconds", Hidden)!.Invoke(Battle, [Rows[actor], Rows[target]])!;
        public float Range(int actor, int target) =>
            (float)Type.GetMethod("WorkingRange", Hidden)!.Invoke(Battle, [Rows[actor], Rows[target]])!;
        public void FillCandidates(int actor) => Type.GetMethod("FillTargetCandidates", Hidden)!.Invoke(Battle, [actor]);
        public EntityKey Select(int actor, Squad? squad, Brain brain, out float cost)
        {
            object?[] args = [actor, squad, brain, 0f];
            var target = (EntityKey)Type.GetMethod("SelectTarget", Hidden)!.Invoke(Battle, args)!;
            cost = (float)args[3]!; return target;
        }
        public void Dispose() => Battle.Dispose();
    }

    private static void CopyChecks<TStore>() where TStore : IEcsStore
    {
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(op => unchecked((ushort)op.Value));
        var type = typeof(Battle<TStore>);
        foreach (var name in new[] { "PrepareTactics", "NewSquad", "TargetAvailable", "SelectTarget", "TargetCost",
                     "EstimateTarget", "WeaponArrival", "AddMemberStreams", "AddStream", "MemberEngageSeconds", "WorkingRange",
                     "PrepareThreatIndex", "CoverVelocity", "FormationSteering" })
        {
            var method = type.GetMethod(name, Hidden)!;
            var il = method.GetMethodBody()!.GetILAsByteArray()!;
            for (var offset = 0; offset < il.Length;)
            {
                ushort code = il[offset++];
                if (code == 0xfe) code = (ushort)(0xfe00 | il[offset++]);
                var op = opcodes[code];
                if (op.OperandType == OperandType.InlineMethod)
                {
                    var called = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset), type.GetGenericArguments(), null)!;
                    Check(called.DeclaringType != typeof(List<AiShipSnapshot>) || called.Name != "get_Item",
                        name + " must not copy a snapshot through List.get_Item");
                }
                if (op == OpCodes.Ldobj && name is "SelectTarget" or "TargetCost" or "EstimateTarget" or "WeaponArrival" or "AddStream" or "TargetAvailable")
                    Check(method.Module.ResolveType(BitConverter.ToInt32(il, offset), type.GetGenericArguments(), null) != typeof(AiShipSnapshot),
                        name + " must borrow the snapshot rather than ldobj-copy it");
                offset += op.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                    _ => 4
                };
            }
        }
    }

    // Independent v3 reference: allocating event list, direct uncached arithmetic and layer integration.
    // It does not call EstimateTarget, TargetCost, formation helpers or the central kill-time estimator.
    private sealed class ScoringOracle(AiShipSnapshot[] rows, BattleConfig config, int tick, float[] pendingShield, float[] pendingHull)
    {
        private readonly record struct Fire(int Member, int Order, float Arrival, float Volley, float Rate, DamageProfile Profile);
        public EntityKey Select(int actor, Squad? squad, Brain brain, out float cost, out bool retained)
        {
            var currentIndex = Array.FindIndex(rows, r => r.Entity == brain.Target && !r.Docked && r.Id.Team != rows[actor].Id.Team);
            var current = currentIndex >= 0 ? Cost(actor, squad, currentIndex) : float.PositiveInfinity;
            retained = false;
            if (float.IsFinite(current) && tick < brain.TargetLockUntilTick)
            { retained = true; cost = current; return brain.Target; }
            float best = float.PositiveInfinity; var target = EntityKey.None;
            for (var i = 0; i < rows.Length; i++)
            {
                if (rows[i].Docked || rows[i].Id.Team == rows[actor].Id.Team) continue;
                var candidate = Cost(actor, squad, i);
                if (candidate < best) { best = candidate; target = rows[i].Entity; }
            }
            if (float.IsFinite(current) && (target == brain.Target || best >= current * (1 - TacticalRules.SwitchImprovement)))
            { retained = true; cost = current; return brain.Target; }
            cost = float.IsFinite(best) ? best : 0; return target;
        }
        public float Cost(int actor, Squad? squad, int target) => Estimate(actor, squad, target).Cost;
        public TacticalEstimate Estimate(int actorIndex, Squad? squad, int targetIndex)
        {
            var target = rows[targetIndex]; var members = new List<int>(); var shots = new List<Fire>();
            var count = squad?.Count ?? 1;
            if (count < 1 || count > TacticalRules.MaxSquadSize) return TacticalEstimate.Unavailable;
            var roster = Enumerable.Range(0, count).Select(s => squad is null ? actorIndex : squad.Value.Members[s]).Order().ToArray();
            for (var s = 0; s < count; s++)
            {
                var member = roster[s]; var actor = rows[member];
                if (actor.Docked || count > 1 && actor.Brain.Mode != BrainMode.Attack) continue;
                members.Add(member);
                var scale = Scale(actor.Heat, HeatRate(actor.Primary) + HeatRate(actor.Secondary), FirstHeat(actor.Primary) + FirstHeat(actor.Secondary));
                float primaryFire = float.PositiveInfinity; float primaryHeat = 0;
                if (Arrival(actor, target, actor.Primary, out var distance, out var time, out var flight))
                { primaryFire = time - flight;
                    var burst = Burst(actor.Primary, actor.Heat.Limit - MathF.Max(0, actor.Heat.Value - actor.Heat.CoolingPerSecond * primaryFire));
                    primaryHeat = actor.Primary.HeatPerShot * burst;
                    Add(shots, member, target, actor.Primary, distance, time, flight, scale, 1, burst); }
                if (Arrival(actor, target, actor.Secondary, out distance, out time, out flight))
                {
                    var ready = time - flight;
                    var secondaryHeat = SecondHeat(actor, primaryFire, primaryHeat, ready);
                    var floor = secondaryHeat < actor.Heat.Limit ? ready : actor.Heat.CoolingPerSecond > 0
                        ? ready + (secondaryHeat - actor.Heat.Limit) / actor.Heat.CoolingPerSecond + 1f / config.TickRate : float.PositiveInfinity;
                    if (Arrival(actor, target, actor.Secondary, out distance, out time, out flight, floor))
                        Add(shots, member, target, actor.Secondary, distance, time, flight, scale, 1,
                            Burst(actor.Secondary, actor.Heat.Limit - SecondHeat(actor, primaryFire, primaryHeat, time - flight)));
                }
                if (count == 1)
                {
                    var pairs = ShipCatalog.TurretCount(actor.Id.Class) / 2;
                    for (var mount = 0; mount < 2 && pairs > 0; mount++)
                    {
                        var weapon = ShipCatalog.TurretWeapon(actor.Id.Class, mount);
                        if (Arrival(actor, target, weapon, out distance, out time, out flight))
                            Add(shots, member, target, weapon, distance, time, flight,
                                Scale(new Heat { Limit = 30, CoolingPerSecond = 12 }, HeatRate(weapon), FirstHeat(weapon)), pairs, Burst(weapon, 30));
                    }
                }
            }
            if (shots.Count == 0) return TacticalEstimate.Unavailable;
            shots = shots.OrderBy(s => s.Arrival).ThenBy(s => s.Member).ThenBy(s => s.Order).ToList();
            var first = shots[0].Arrival;
            if (count > 1) shots.RemoveAll(s => s.Arrival > first + config.Tactics.MaximumJoinDelaySeconds);
            var horizon = first + config.Tactics.ScoringHorizonSeconds;
            var shield = MathF.Max(0, target.Shield.Current) - Math.Clamp(pendingShield[targetIndex], 0, MathF.Max(0, target.Shield.Current) * 0.5f);
            var hull = MathF.Max(0, target.Hull.Current) - Math.Clamp(pendingHull[targetIndex], 0, MathF.Max(0, target.Hull.Current) * 0.5f);
            float sr = 0, hr = 0, heatRate = 0, heat = 0, now = 0, kill = float.PositiveInfinity;
            var armor = MathF.Max(0, 1 - target.Hull.Armor);
            foreach (var shot in shots)
            {
                if (shot.Arrival > horizon) break;
                if (Flow(shot.Arrival - now, sr, hr * armor, heatRate, ref shield, ref hull, ref heat, out var elapsed))
                { kill = now + elapsed; break; }
                now = shot.Arrival;
                var spent = MathF.Min(shot.Volley, shield / shot.Profile.ShieldMultiplier);
                var removed = MathF.Min(shield, spent * shot.Profile.ShieldMultiplier);
                shield = MathF.Max(0, shield - removed);
                hull = MathF.Max(0, hull - (shot.Volley - spent) * shot.Profile.HullMultiplier * (1 - target.Hull.Armor));
                heat += removed * shot.Profile.ShieldHeatPerDamage;
                if (shield <= 0 && hull <= 0) { kill = now; break; }
                sr += shot.Rate * shot.Profile.ShieldMultiplier; hr += shot.Rate * shot.Profile.HullMultiplier;
                heatRate += shot.Rate * shot.Profile.ShieldMultiplier * shot.Profile.ShieldHeatPerDamage;
            }
            if (!float.IsFinite(kill))
            {
                if (Flow(horizon - now, sr, hr * armor, heatRate, ref shield, ref hull, ref heat, out var elapsed))
                    kill = now + elapsed;
                else
                {
                    kill = horizon + LayerTime(shield, hull, target.Hull.Armor, sr, hr);
                }
            }
            if (!float.IsFinite(kill)) return TacticalEstimate.Unavailable;
            var activeMembers = shots.Where(s => s.Arrival <= MathF.Min(horizon, kill)).Select(s => s.Member).Distinct().Count();
            float losses = 0, readyTarget = float.PositiveInfinity;
            foreach (var member in members)
            {
                var incoming = shots.Where(s => s.Member == member && s.Arrival <= MathF.Min(horizon, kill)).ToArray();
                if (incoming.Length == 0) continue;
                var actor = rows[member]; var arrival = incoming.Min(s => s.Arrival);
                readyTarget = MathF.Min(readyTarget, Engage(target, actor));
                if (target.Brain.Mode != BrainMode.Attack) continue;
                var exposure = MathF.Min(config.Tactics.ScoringHorizonSeconds, MathF.Max(0, kill - arrival));
                var focused = target.Brain.Target == actor.Entity ||
                    actor.Combat.LastAttacker == target.Entity && tick - actor.Combat.LastDamageTick <= 180;
                losses += Loss(target, actor, exposure) * (focused ? 1 : 1f / Math.Max(1, activeMembers));
            }
            var window = MathF.Max(1f / config.TickRate, MathF.Min(kill, config.Tactics.ScoringHorizonSeconds));
            var budget = MathF.Max(0.01f, target.Heat.Limit - target.Heat.Value + target.Heat.CoolingPerSecond * window);
            var opportunity = target.Brain.Mode == BrainMode.Attack && float.IsFinite(readyTarget) && !target.Overheated
                ? Math.Clamp((window - readyTarget) / window, 0, 1) : 0;
            var suppression = config.Tactics.MaximumSuppressionBenefit * Math.Clamp(heat / budget, 0, 1) * opportunity;
            return new(kill, suppression, losses, kill * (1 + config.Tactics.ThreatWeight * losses) * (1 - suppression));
        }
        private static bool Flow(float duration, float sr, float hr, float heatRate,
            ref float shield, ref float hull, ref float heat, out float elapsed)
        {
            elapsed = 0;
            if (shield <= 0 && hull <= 0) return true;
            if (duration <= 0) return false;
            if (shield > 0)
            {
                if (!(sr > 0)) return false;
                var finish = shield / sr; var spent = MathF.Min(duration, finish);
                var damage = MathF.Min(shield, sr * spent);
                shield = MathF.Max(0, shield - damage); heat += damage * heatRate / sr;
                elapsed += spent; duration -= spent;
                if (finish > spent) return false;
                shield = 0;
            }
            if (hull <= 0) return true;
            if (!(hr > 0)) return false;
            var finishHull = hull / hr;
            if (finishHull <= duration) { elapsed += finishHull; hull = 0; return true; }
            hull = MathF.Max(0, hull - hr * duration); return false;
        }
        private static float LayerTime(float shield, float hull, float armor, float sr, float hr)
        {
            var st = shield > 0 ? sr > 0 ? shield / sr : float.PositiveInfinity : 0;
            var armored = hr * (1 - armor);
            return st + (hull > 0 ? armored > 0 ? hull / armored : float.PositiveInfinity : 0);
        }
        private static bool Available(Weapon w) => w.NextFireTick != int.MaxValue && w.Cooldown > 0 && w.Burst > 0 &&
            w.Damage > 0 && float.IsFinite(w.Damage) && w.Speed > 0 && float.IsFinite(w.Speed) && w.Range > 0 && float.IsFinite(w.Range);
        private float HeatRate(Weapon w) => !Available(w) ? 0 : w.HeatPerShot * w.Burst * config.TickRate * config.ChurnMultiplier / w.Cooldown;
        private static float FirstHeat(Weapon w) => Available(w) ? MathF.Max(0, w.HeatPerShot * w.Burst) : 0;
        private float Scale(Heat h, float rate, float firstHeat) => rate <= 0 ? 1 : Math.Clamp((MathF.Max(0, h.CoolingPerSecond) +
            MathF.Max(0, h.Limit - h.Value - firstHeat) / config.Tactics.ScoringHorizonSeconds) / rate, 0, 1);
        private float Engage(AiShipSnapshot a, AiShipSnapshot t)
        {
            var first = float.PositiveInfinity;
            foreach (var w in new[] { a.Primary, a.Secondary })
                if (Arrival(a, t, w, out _, out var arrival, out _)) first = MathF.Min(first, arrival);
            if (ShipCatalog.TurretCount(a.Id.Class) > 0)
                for (var i = 0; i < 2; i++)
                    if (Arrival(a, t, ShipCatalog.TurretWeapon(a.Id.Class, i), out _, out var arrival, out _)) first = MathF.Min(first, arrival);
            return first;
        }
        private bool Arrival(AiShipSnapshot a, AiShipSnapshot t, Weapon w, out float distance, out float arrival, out float flight, float floor = 0)
        {
            distance = 0; arrival = float.PositiveInfinity; flight = 0;
            if (!Available(w) || a.Docked || !(a.Heat.Limit > 0)) return false;
            float ready = 0;
            if (a.Overheated || a.Heat.Value >= a.Heat.Limit)
            {
                if (!(a.Heat.CoolingPerSecond > 0)) return false;
                var threshold = a.Overheated ? a.Heat.Limit * 0.45f : a.Heat.Limit;
                ready = MathF.Max(0, (a.Heat.Value - threshold) / a.Heat.CoolingPerSecond) + 1f / config.TickRate;
            }
            ready = MathF.Max(floor, MathF.Max(ready,
                MathF.Max(0, (long)w.NextFireTick - tick) / (float)config.TickRate));
            if (ready > config.Tactics.MaximumEngageSeconds) return false;
            var future = t.Transform.Position + t.Motion.Velocity * ready;
            var origin = a.Transform.Position + a.Motion.Velocity * ready;
            distance = Vector2.Distance(origin, future);
            if (distance > w.Range)
            {
                if (!(a.Motion.MaxSpeed > 0.5f) || ShipCatalog.IsCarrier(a.Id.Class) && distance > w.Range * 1.5f) return false;
                var pursuit = FireControl.Predict(origin, Vector2.Zero, future, t.Motion.Velocity, a.Motion.MaxSpeed, w.Range * 0.9f);
                var extra = pursuit.InterceptTime;
                if (!(extra > 0) || !float.IsFinite(extra)) return false;
                ready += extra;
                if (ready > config.Tactics.MaximumEngageSeconds) return false;
                origin += pursuit.Direction * a.Motion.MaxSpeed * extra;
                future += t.Motion.Velocity * extra;
                distance = Vector2.Distance(origin, future);
                if (distance > w.Range * 1.0001f) return false;
            }
            var aim = FireControl.Predict(origin, a.Motion.Velocity, future, t.Motion.Velocity,
                w.Kind == WeaponKind.Missile ? 85 : w.Speed, a.Radius + 0.9f);
            if (distance > a.Radius + t.Radius + 0.9f && (aim.InterceptTime <= 0 || aim.InterceptTime > 4)) return false;
            flight = MathF.Max(1f / config.TickRate, aim.InterceptTime); arrival = ready + flight;
            return float.IsFinite(arrival) && arrival <= config.Tactics.MaximumEngageSeconds;
        }
        private static float Chance(AiShipSnapshot target, float distance, float flight)
        {
            var r = MathF.Max(0.01f, target.Radius);
            var maneuver = r / (r + target.Motion.Velocity.Length() * flight * 0.025f);
            var spread = distance * MathF.Tan(FireControl.MaxSpreadRadians);
            return Math.Clamp(maneuver * (0.75f + 0.25f * (spread > 0 ? Math.Clamp(r / spread, 0, 1) : 1)), 0.01f, 1);
        }
        private void Add(List<Fire> output, int member, AiShipSnapshot target, Weapon w, float distance, float arrival, float flight, float scale, int copies, int firstBurst)
        {
            if (firstBurst <= 0) return;
            var p = DamageRules.ForWeapon(w.Kind, config.Damage); var probability = Chance(target, distance, flight);
            var armor = MathF.Max(0, 1 - target.Hull.Armor);
            var capacity = MathF.Max(0, target.Shield.Current) / p.ShieldMultiplier +
                (target.Hull.Current <= 0 ? 0 : armor > 0 ? target.Hull.Current / (p.HullMultiplier * armor) : float.PositiveInfinity);
            var shots = w.Burst * copies; var raw = w.Damage * shots;
            var sustainable = MathF.Min(raw * probability, capacity * (1 - MathF.Pow(1 - probability, shots)));
            var firstShots = firstBurst * copies;
            var volley = MathF.Min(w.Damage * firstShots * probability, capacity * (1 - MathF.Pow(1 - probability, firstShots)));
            if (!(volley > 0) || !float.IsFinite(volley)) return;
            var baseDps = w.Damage * w.Burst * config.TickRate * config.ChurnMultiplier / w.Cooldown;
            var ticks = Math.Max(1, (int)(w.Cooldown / config.ChurnMultiplier));
            var correction = w.Cooldown / config.ChurnMultiplier / ticks;
            var cap = sustainable / (raw * probability);
            output.Add(new(member, output.Count, arrival, volley, baseDps * copies * probability * cap * correction * scale, p));
        }
        private static int Burst(Weapon w, float headroom) => headroom <= 0 ? 0 : w.HeatPerShot <= 0
            ? w.Burst : (int)Math.Clamp(MathF.Ceiling(headroom / w.HeatPerShot), 0, w.Burst);
        private static float SecondHeat(AiShipSnapshot a, float first, float firstHeat, float fire) => first <= fire
            ? MathF.Max(0, MathF.Max(0, a.Heat.Value - a.Heat.CoolingPerSecond * first) + firstHeat -
                a.Heat.CoolingPerSecond * (fire - first)) : MathF.Max(0, a.Heat.Value - a.Heat.CoolingPerSecond * fire);
        private float Loss(AiShipSnapshot enemy, AiShipSnapshot actor, float exposure)
        {
            if (exposure <= 0) return 0;
            float sr = 0, hr = 0;
            var scale = Scale(enemy.Heat, HeatRate(enemy.Primary) + HeatRate(enemy.Secondary), FirstHeat(enemy.Primary) + FirstHeat(enemy.Secondary));
            Counter(enemy, actor, enemy.Primary, scale, exposure, 1, ref sr, ref hr);
            Counter(enemy, actor, enemy.Secondary, scale, exposure, 1, ref sr, ref hr);
            var pairs = ShipCatalog.TurretCount(enemy.Id.Class) / 2;
            for (var i = 0; i < 2 && pairs > 0; i++)
            {
                var w = ShipCatalog.TurretWeapon(enemy.Id.Class, i);
                Counter(enemy, actor, w, Scale(new Heat { Limit = 30, CoolingPerSecond = 12 }, HeatRate(w), FirstHeat(w)), exposure, pairs, ref sr, ref hr);
            }
            var time = LayerTime(actor.Shield.Current, actor.Hull.Current, actor.Hull.Armor, sr, hr);
            return time > 0 ? Math.Clamp(1 / time, 0, 1) : 1;
        }
        private void Counter(AiShipSnapshot enemy, AiShipSnapshot actor, Weapon w, float scale, float exposure, int copies, ref float sr, ref float hr)
        {
            if (!Arrival(enemy, actor, w, out var distance, out var arrival, out var flight) || arrival > exposure) return;
            var p = DamageRules.ForWeapon(w.Kind, config.Damage); var chance = Chance(actor, distance, flight);
            var ticks = Math.Max(1, (int)(w.Cooldown / config.ChurnMultiplier));
            var volleys = 1 + MathF.Max(0, exposure - arrival) * config.TickRate / ticks * scale;
            var damage = w.Damage * w.Burst * copies * chance * volleys;
            sr += damage * p.ShieldMultiplier; hr += damage * p.HullMultiplier;
        }
    }
}
