using System.Collections;
using System.Numerics;
using System.Reflection;
using SpaceBattle;

internal static partial class Validation
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type BattleType = typeof(Battle<SkecsStore>);
    private readonly record struct Row(EntityKey Entity, Identity Id, Transform Transform, Hull Hull,
        Shield Shield, Heat Heat, Weapon Primary, Weapon Secondary, Brain Brain, CombatState Combat, float Radius, bool Docked);

    private static void TacticalSpatialChecks()
    {
        PointGridChecks();
        FormationChecks();
        ExistingSquadSpatialChecks();
        ThreatAndCoverChecks();
        Console.WriteLine("PASS: tactical point candidates, formation boundaries/ties, snapshot threat/cover differential checks and cache rebuilds.");
    }

    private static void PointGridChecks()
    {
        var grid = new SpatialPointIndex();
        var output = new List<int>(); var random = new Random(7331);
        var points = new List<Vector2> { new(-48, -48), new(0, 0), new(48, 0), new(0, 48), new(-0.001f, 0), new(96, 96) };
        for (var i = 0; i < 300; i++) points.Add(new(random.Next(-400, 401), random.Next(-400, 401)));
        // Deliberately insert out of order; queries must restore snapshot order.
        for (var i = points.Count - 1; i >= 0; i--) grid.Add(points[i], i);
        for (var i = 0; i < 160; i++)
        {
            var position = i < points.Count / 2 ? points[i] : new Vector2(random.Next(-500, 501), random.Next(-500, 501));
            var radius = i % 4 == 0 ? 0 : i % 4 == 1 ? 48 : random.Next(1, 200);
            grid.Query(position, radius, output);
            Require(output.SequenceEqual(output.Distinct().Order()), "point candidates unique and ascending");
            for (var j = 0; j < points.Count; j++)
                if (MathF.Abs(position.X - points[j].X) <= radius && MathF.Abs(position.Y - points[j].Y) <= radius)
                    Require(output.Contains(j), "point grid conservatively includes AABB boundary and corners");
        }
        grid.Clear(); grid.Add(new Vector2(-960, 960), 7);
        grid.Query(Vector2.Zero, 48f, output); Require(output.Count == 0, "rebuild removes old cells");
        grid.Query(new Vector2(-960, 960), 0f, output); Require(output.SequenceEqual(new[] { 7 }), "reused bucket contains only new points");
        grid.Query(Vector2.Zero, float.MaxValue, output); Require(output.SequenceEqual(new[] { 7 }), "huge query remains bounded by populated cells");
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            grid.Clear(); grid.Add(new(-960, 960), 7); grid.Query(new(-960, 960), 48, output);
        }
        Require(GC.GetAllocatedBytesForCurrentThread() == before, "warmed spatial index reuses buckets and candidate buffer without allocations");
    }

    private static void FormationChecks()
    {
        using var battle = CreateBattle();
        var leader = Spawn(battle, 0, new(-48, -48), true);
        var first = Spawn(battle, 0, new(-38, -48), true);
        var tied = Spawn(battle, 0, new(-58, -48), true);
        var boundary = Spawn(battle, 0, new(0, -48), true);
        Spawn(battle, 1, new(20, -48), false);
        Prepare(battle);
        Require(battle.Store.Get<Brain>(leader).SquadLeader == leader &&
            battle.Store.Get<Brain>(first).SquadLeader == leader && battle.Store.Get<Brain>(tied).SquadLeader == leader,
            "compatible formation candidates retain stable serial order");
        Require(battle.Store.Get<Brain>(boundary).SquadLeader.IsNone, "formation uses strict less-than at 48 boundary");
        var squads = (IList)BattleType.GetField("squadBuffers", PrivateInstance)!.GetValue(battle)!;
        var squad = squads[0]!;
        var members = ((Squad)squad).Members;
        var rows = Rows(battle);
        Require(rows[members[1]].Entity == first && rows[members[2]].Entity == tied, "equal-distance member slot order unchanged");
        battle.Store.Get<Transform>(tied).Position = new(400, 400);
        Prepare(battle);
        Require(battle.Store.Get<Brain>(tied).SquadLeader.IsNone, "formation rebuild honors moved snapshot and broken squad");
    }

    private static void ExistingSquadSpatialChecks()
    {
        using (var battle = CreateBattle())
        {
            // A is restored first through its earlier member, despite B's earlier leader serial.
            var memberA = Spawn(battle, 0, new(-10, 1), true);
            var leaderB = Spawn(battle, 0, new(10, 0), true);
            var memberB = Spawn(battle, 0, new(10, 1), true);
            var leaderA = Spawn(battle, 0, new(-10, 0), true);
            var enemyLeader = Spawn(battle, 1, Vector2.Zero, true);
            var enemyMember = Spawn(battle, 1, new(0, 1), true);
            var first = Spawn(battle, 0, Vector2.Zero, true);
            var second = Spawn(battle, 0, Vector2.Zero, true);
            var third = Spawn(battle, 0, Vector2.Zero, true);
            var boundary = Spawn(battle, 0, new(-58, 0), true);
            foreach (var e in new[] { leaderA, memberA }) battle.Store.Get<Brain>(e).SquadLeader = leaderA;
            foreach (var e in new[] { leaderB, memberB }) battle.Store.Get<Brain>(e).SquadLeader = leaderB;
            foreach (var e in new[] { enemyLeader, enemyMember }) battle.Store.Get<Brain>(e).SquadLeader = enemyLeader;
            Prepare(battle);
            Require(battle.Store.Get<Brain>(first).SquadLeader == leaderA &&
                battle.Store.Get<Brain>(second).SquadLeader == leaderA,
                "existing-squad distance ties use original g order, not leader snapshot order; enemy leader skipped");
            Require(battle.Store.Get<Brain>(third).SquadLeader == leaderB, "full nearest tied squad skipped immediately after same-loop additions");
            Require(battle.Store.Get<Brain>(boundary).SquadLeader.IsNone, "existing-squad exact 48 boundary remains excluded");
            var grid = (SpatialPointIndex)BattleType.GetField("squadLeaderPoints", PrivateInstance)!.GetValue(battle)!;
            var candidates = new List<int>();
            grid.Query(new(-58, 0), 0, candidates);
            var squads = (IList)BattleType.GetField("squadBuffers", PrivateInstance)!.GetValue(battle)!;
            Require(candidates.Any(g => (int)squads[g]!.GetType().GetField("Count")!.GetValue(squads[g])! == 0),
                "failed fresh singleton remains indexed with count zero for original skip semantics");
            foreach (var e in new[] { leaderA, memberA, leaderB, memberB, enemyLeader, enemyMember, first, second, third, boundary })
            {
                battle.Store.Get<Brain>(e).NextDecisionTick = int.MaxValue;
                battle.Store.Get<Brain>(e).SquadLeader = EntityKey.None;
            }
            Prepare(battle);
            grid.Query(Vector2.Zero, 1000, candidates);
            Require(candidates.Count == 0, "leader grid cleared before next snapshot restoration");
        }
        using (var battle = CreateBattle())
        {
            var leader = Spawn(battle, 0, new(240, 240), true);
            var member = Spawn(battle, 0, new(241, 240), true);
            Spawn(battle, 1, new(270, 240), false);
            Prepare(battle);
            Require(battle.Store.Get<Brain>(member).SquadLeader == leader, "fresh squad created in formation loop");
            var grid = (SpatialPointIndex)BattleType.GetField("squadLeaderPoints", PrivateInstance)!.GetValue(battle)!;
            var candidates = new List<int>(); grid.Query(new(240, 240), 0, candidates);
            Require(candidates.SequenceEqual(new[] { 0 }), "NewSquad indexes fresh leader immediately by g");
        }
    }

    private static void ThreatAndCoverChecks()
    {
        using var battle = CreateBattle();
        var random = new Random(27); var actors = new List<EntityKey>();
        for (var i = 0; i < 70; i++) actors.Add(Spawn(battle, i % 2,
            new(random.Next(-120, 121), random.Next(-120, 121)), false));
        for (var i = 0; i < actors.Count; i++)
        {
            var e = actors[i]; ref var brain = ref battle.Store.Get<Brain>(e);
            brain.Mode = i % 5 == 0 ? BrainMode.Retreat : BrainMode.Attack;
            brain.Target = actors[random.Next(actors.Count)];
            battle.Store.Get<Shield>(e).Current = i % 4 == 0 ? 0 : 50;
            battle.Store.Get<Heat>(e).Value = i % 7 == 0 ? 99 : 0;
            battle.Store.Get<CombatState>(e) = new CombatState { LastDamageTick = 0, LastAttacker = actors[random.Next(actors.Count)] };
        }
        var victim = actors[0]; var first = actors[1]; var tied = actors[3];
        battle.Store.Get<Transform>(victim).Position = Vector2.Zero;
        battle.Store.Get<Transform>(first).Position = new(10, 0);
        battle.Store.Get<Transform>(tied).Position = new(-10, 0);
        foreach (var e in actors.Where((_, i) => i % 2 == 1)) battle.Store.Get<Brain>(e).Target = EntityKey.None;
        battle.Store.Get<Brain>(first).Target = victim; battle.Store.Get<Brain>(tied).Target = victim;
        battle.Store.Get<CombatState>(victim).LastAttacker = EntityKey.None;
        battle.Store.Get<Brain>(actors[2]).Target = victim; // Same-team incoming edge must not be a threat.
        battle.Store.Get<Transform>(actors[2]).Position = Vector2.Zero;
        Prepare(battle);
        CompareSnapshot(battle);
        var index = Rows(battle).FindIndex(row => row.Entity == victim);
        Require(Threat(battle, index) == first, "nearest incoming threat uses original tie order");
        battle.Store.Get<Brain>(first).Target = EntityKey.None;
        battle.Store.Get<Transform>(first).Position = new(900, 900);
        Require(Threat(battle, index) == first, "memoized threat ignores live post-snapshot mutation");
        Prepare(battle); CompareSnapshot(battle);
        Require(Threat(battle, index) == tied, "next snapshot invalidates cached threat");
        battle.Store.Get<CombatState>(victim) = new CombatState { LastDamageTick = 0, LastAttacker = actors[5] };
        battle.Store.Get<Transform>(actors[5]).Position = new(30, 0);
        battle.Store.Get<Brain>(actors[5]).Mode = BrainMode.Patrol;
        Prepare(battle); CompareSnapshot(battle);
        Require(Threat(battle, index) == actors[5], "recent attacker priority does not require attack mode or targeting victim");
        battle.Store.Set(actors[5], new Docked { Carrier = actors[2] });
        Prepare(battle); CompareSnapshot(battle);
        Require(Threat(battle, index) == tied, "docked recent attacker unavailable");
        battle.Store.Get<Brain>(tied).Target = EntityKey.None;
        Prepare(battle);
        Require(Threat(battle, index).IsNone && Threat(battle, index).IsNone, "no-threat result is reusable");
        battle.Store.Get<Brain>(tied).Target = victim;
        Prepare(battle);
        Require(Threat(battle, index) == tied, "no-threat cache invalidated on next snapshot");

        // Cover can extend beyond formation radius by the candidate ally's collider radius.
        foreach (var e in actors) battle.Store.Get<Brain>(e).Mode = BrainMode.Retreat;
        var cover = actors[2];
        battle.Store.Get<Transform>(cover).Position = new(58, 0);
        battle.Store.Get<Collider>(cover).Radius = 12;
        battle.Store.Get<Brain>(cover).Mode = BrainMode.Patrol;
        battle.Store.Get<Heat>(cover).Value = 0;
        battle.Store.Get<Shield>(cover).Current = battle.Store.Get<Shield>(cover).Maximum;
        battle.Store.Get<Hull>(victim).Current *= 0.5f;
        Prepare(battle); CompareSnapshot(battle);
        Require(Cover(battle, index) == cover, "cover broadphase includes ally radius beyond 48");
    }

    private static Battle<SkecsStore> CreateBattle()
    {
        var battle = new Battle<SkecsStore>(new(), new BattleConfig { ShipsPerTeam = 16, Seed = 27,
            Tactics = new TacticsSettings { MemberCandidates = 64 } });
        var frame = new BattleFrame(); battle.Capture(frame);
        foreach (var ship in frame.Ships)
        {
            Require(battle.TryFindShip(ship.Serial, out var e), "spatial fixture ship lookup");
            battle.Store.Get<Brain>(e).Mode = BrainMode.Retreat;
            battle.Store.Get<Brain>(e).NextDecisionTick = int.MaxValue;
            battle.Store.Get<Brain>(e).Target = EntityKey.None;
            battle.Store.Get<Transform>(e).Position = new(10000 + ship.Serial * 50, 10000);
        }
        return battle;
    }

    private static EntityKey Spawn(Battle<SkecsStore> battle, int team, Vector2 position, bool formation)
    {
        var e = battle.SpawnShip(team, ShipClass.Fighter, position, EntityKey.None);
        battle.Store.Get<Brain>(e) = new Brain { Mode = BrainMode.Attack, NextDecisionTick = formation ? 0 : int.MaxValue,
            Target = EntityKey.None, Home = EntityKey.None, SquadLeader = EntityKey.None, CoverAlly = EntityKey.None };
        battle.Store.Get<Hull>(e) = new Hull { Current = 100, Maximum = 100 };
        battle.Store.Get<Shield>(e) = new Shield { Current = 50, Maximum = 50 };
        battle.Store.Get<Heat>(e) = new Heat { Limit = 100 };
        return e;
    }

    private static void Prepare(Battle<SkecsStore> battle)
    {
        battle.Store.Commit(); battle.Capture(new BattleFrame());
        BattleType.GetMethod("PrepareTactics", PrivateInstance)!.Invoke(battle, null);
    }
    private static EntityKey Threat(Battle<SkecsStore> battle, int index) =>
        (EntityKey)BattleType.GetMethod("FindThreat", PrivateInstance)!.Invoke(battle, [index])!;
    private static EntityKey Cover(Battle<SkecsStore> battle, int index) =>
        (EntityKey)BattleType.GetMethod("FindCover", PrivateInstance)!.Invoke(battle, [index])!;

    private static List<Row> Rows(Battle<SkecsStore> battle)
    {
        var result = new List<Row>();
        foreach (var value in (IEnumerable)BattleType.GetField("aiRows", PrivateInstance)!.GetValue(battle)!)
        {
            T Read<T>(string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;
            result.Add(new(Read<EntityKey>("Entity"), Read<Identity>("Id"), Read<Transform>("Transform"), Read<Hull>("Hull"),
                Read<Shield>("Shield"), Read<Heat>("Heat"), Read<Weapon>("Primary"), Read<Weapon>("Secondary"),
                Read<Brain>("Brain"), Read<CombatState>("Combat"), Read<float>("Radius"), Read<bool>("Docked")));
        }
        return result;
    }

    private static void CompareSnapshot(Battle<SkecsStore> battle)
    {
        var rows = Rows(battle);
        for (var i = 0; i < rows.Count; i++)
        {
            Require(Threat(battle, i) == ReferenceThreat(rows, i, battle.Tick), "incoming threat matches original full scan");
            Require(Cover(battle, i) == ReferenceCover(rows, i, battle.Tick), "cover candidates match original full scan");
            Require(Threat(battle, i) == ReferenceThreat(rows, i, battle.Tick), "memoized threat matches repeated full scan");
        }
    }

    private static EntityKey ReferenceThreat(List<Row> rows, int index, int tick)
    {
        var row = rows[index];
        var attacker = rows.FindIndex(r => r.Entity == row.Combat.LastAttacker && !r.Docked && r.Id.Team != row.Id.Team);
        if (tick - row.Combat.LastDamageTick <= 180 && attacker >= 0 &&
            Vector2.DistanceSquared(row.Transform.Position, rows[attacker].Transform.Position) <
            MathF.Pow(MathF.Max(rows[attacker].Primary.Range, rows[attacker].Secondary.Range) * 1.3f, 2)) return rows[attacker].Entity;
        var nearest = float.PositiveInfinity; var threat = EntityKey.None;
        foreach (var enemy in rows)
        {
            if (enemy.Id.Team == row.Id.Team || enemy.Docked || enemy.Brain.Mode != BrainMode.Attack || enemy.Brain.Target != row.Entity) continue;
            var distance = Vector2.DistanceSquared(row.Transform.Position, enemy.Transform.Position);
            var range = MathF.Max(enemy.Primary.Range, enemy.Secondary.Range) * 1.1f;
            if (distance < range * range && distance < nearest) { nearest = distance; threat = enemy.Entity; }
        }
        return threat;
    }

    private static EntityKey ReferenceCover(List<Row> rows, int index, int tick)
    {
        var row = rows[index]; var vitality = Vitality(row); var best = float.NegativeInfinity; var cover = EntityKey.None;
        for (var i = 0; i < rows.Count; i++)
        {
            var ally = rows[i];
            if (ally.Id.Team != row.Id.Team || ally.Entity == row.Entity || ally.Docked ||
                ally.Brain.Mode is BrainMode.Screen or BrainMode.Retreat or BrainMode.Docking ||
                ally.Hull.Current < ally.Hull.Maximum * 0.65f || Vitality(ally) < vitality + 0.15f) continue;
            if (ally.Heat.Value > ally.Heat.Limit * 0.92f ||
                (ally.Hull.Current < ally.Hull.Maximum * 0.65f || ally.Shield.Current < ally.Shield.Maximum * 0.18f) &&
                !ReferenceThreat(rows, i, tick).IsNone) continue;
            var distance = Vector2.Distance(row.Transform.Position, ally.Transform.Position);
            if (distance > TacticalRules.FormationRange + ally.Radius) continue;
            var sameGroup = !row.Brain.SquadLeader.IsNone && ally.Brain.SquadLeader == row.Brain.SquadLeader;
            var score = Vitality(ally) + (sameGroup ? 0.5f : 0) - distance * 0.008f;
            if (score > best) { best = score; cover = ally.Entity; }
        }
        return cover;
    }
    private static float Vitality(Row row) => row.Hull.Current / row.Hull.Maximum * 0.65f +
        row.Shield.Current / MathF.Max(1, row.Shield.Maximum) * 0.35f;
}
