using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SpaceBattle;

public static class TacticalRules
{
    public const string Revision = "layered-squads-v3";
    public const int MinimumTargetLockTicks = 120, MaxSquadSize = 4;
    public const float FormationRange = 48, SquadBreakRange = 96, SwitchImprovement = 0.35f;
}

public sealed class AiCounters
{
    public long TargetSwitches, TargetRetentions, GroupOrders, CoverOrders;
    public long GroupChanges, FormationReviews, CompatibilityChecks, ScoreEvaluations, SpatialWork, BudgetFallbacks, IntentProjectiles;
}

// Keep this value type nongeneric for Mono WASM AOT calling conventions.
internal readonly record struct AiShipSnapshot(EntityKey Entity, Identity Id, Transform Transform, Motion Motion,
    Hull Hull, Shield Shield, Heat Heat, Weapon Primary, Weapon Secondary, Brain Brain,
    CombatState Combat, float Radius, bool Docked, bool Overheated = false);
[InlineArray(TacticalRules.MaxSquadSize)] internal struct SquadMembers { private int first; }
internal struct Squad
{
    public SquadMembers Members;
    public int Count, Leader;
}
internal struct ActorScoring
{
    public bool Ready, MountHeatReady;
    public float HeatScale;
    public int MountCount;
    public Weapon Cannon, Plasma;
    public float CannonHeatScale, PlasmaHeatScale;
    public WeaponScoring PrimaryDps, SecondaryDps, CannonDps, PlasmaDps;
}
internal struct WeaponScoring
{
    public bool Ready;
    public float Dps;
}

public sealed partial class Battle<TStore>
{
    // Immutable pre-decision rows keep reasoning independent of the ECS query order.
    private readonly List<AiShipSnapshot> aiRows = new();
    // Borrow only after collection, within a phase that cannot resize/clear this list.
    private Span<AiShipSnapshot> AiRows => CollectionsMarshal.AsSpan(aiRows);
    private readonly EntityIndexTable aiIndexes = new(), squadIndexes = new();
    private Squad[] squadBuffers = [];
    private bool[] assignedSquadMembers = [];
    private bool[] recruitmentBlocked = [];
    private ActorScoring[] actorScoring = [];
    private float[] targetSpeeds = [];
    private bool[] targetSpeedReady = [];
    private int[] enemyTargets0 = [], enemyTargets1 = [];
    private int enemyCount0 = -1, enemyCount1 = -1;
    private readonly SpatialPointIndex tacticalPoints = new();
    private readonly SpatialPointIndex teamPoints0 = new(), teamPoints1 = new();
    private readonly List<int> targetCandidates = new(128), formationTargets = new(16), formationMembers = new(64);
    private float[] committedShield = [], committedHull = [], preferredRanges = [];
    private EntityKey[] rangeTargets = [];
    private bool[] rangeReady = [];
    private int spatialWork, scoreWork, formationWork;
    private readonly List<int> tacticalCandidates = new();
    private readonly SpatialPointIndex squadLeaderPoints = new();
    private readonly List<int> squadLeaderCandidates = new();
    private int[] incomingHeads = [], incomingNext = [];
    private EntityKey[] cachedThreats = [];
    private bool[] threatCached = [];
    private float coverQueryRadius;
    private int squadCount, nextFormationTick;

    private void PrepareTactics()
    {
        aiRows.Clear(); aiIndexes.Clear(); squadIndexes.Clear(); squadCount = 0;
        enemyCount0 = enemyCount1 = -1;
        spatialWork = Config.Tactics.MaximumSpatialWorkPerTick;
        maximumTargetSpeed = 0;
        scoreWork = Config.Tactics.MaximumScoreEvaluationsPerTick;
        formationWork = Config.Tactics.MaximumFormationEvaluationsPerReview;
        squadLeaderPoints.Clear();
        foreach (var ship in ships)
        {
            var e = ship.Entity;
            if (!AliveShip(e)) continue;
            aiIndexes.Add(e, aiRows.Count);
            aiRows.Add(new(e, ship.Identity, ship.Transform, Store.Get<Motion>(e), Store.Get<Hull>(e),
                Store.Get<Shield>(e), Store.Get<Heat>(e), Store.Get<Weapon>(e),
                Store.Has<SecondaryWeapon>(e) ? Store.Get<SecondaryWeapon>(e).Value : default,
                Store.Get<Brain>(e), Store.Get<CombatState>(e), Store.Get<Collider>(e).Radius, Store.Has<Docked>(e), Store.Has<Overheated>(e)));
        }
        if (assignedSquadMembers.Length < aiRows.Count)
        {
            Array.Resize(ref assignedSquadMembers, Math.Max(aiRows.Count, assignedSquadMembers.Length * 2));
            Array.Resize(ref recruitmentBlocked, assignedSquadMembers.Length);
        }
        Array.Clear(assignedSquadMembers, 0, aiRows.Count);
        if (actorScoring.Length < aiRows.Count)
        {
            var capacity = Math.Max(aiRows.Count, actorScoring.Length * 2);
            Array.Resize(ref actorScoring, capacity); Array.Resize(ref targetSpeeds, capacity);
            Array.Resize(ref targetSpeedReady, capacity);
            Array.Resize(ref committedShield, capacity); Array.Resize(ref committedHull, capacity);
            Array.Resize(ref preferredRanges, capacity); Array.Resize(ref rangeTargets, capacity); Array.Resize(ref rangeReady, capacity);
        }
        Array.Clear(actorScoring, 0, aiRows.Count); Array.Clear(targetSpeedReady, 0, aiRows.Count);
        Array.Clear(committedShield, 0, aiRows.Count); Array.Clear(committedHull, 0, aiRows.Count); Array.Clear(rangeReady, 0, aiRows.Count);
        // At most N restored groups plus N attempted fresh groups. No resize with a live squad ref.
        var squadCapacity = checked(aiRows.Count * 2);
        if (squadBuffers.Length < squadCapacity)
            Array.Resize(ref squadBuffers, Math.Max(squadCapacity, checked(squadBuffers.Length * 2)));
        var rows = AiRows;
        tacticalPoints.Clear(); coverQueryRadius = TacticalRules.FormationRange;
        teamPoints0.Clear(); teamPoints1.Clear();
        tacticalPoints.Reserve(rows.Length); teamPoints0.Reserve(rows.Length); teamPoints1.Reserve(rows.Length);
        for (var i = 0; i < aiRows.Count; i++)
        {
            ref readonly var row = ref rows[i];
            maximumTargetSpeed = MathF.Max(maximumTargetSpeed, MathF.Abs(row.Motion.Velocity.X) + MathF.Abs(row.Motion.Velocity.Y));
            tacticalPoints.Add(row.Transform.Position, i);
            if (!row.Docked && row.Id.Team == 0) teamPoints0.Add(row.Transform.Position, i);
            if (!row.Docked && row.Id.Team == 1) teamPoints1.Add(row.Transform.Position, i);
            coverQueryRadius = MathF.Max(coverQueryRadius, TacticalRules.FormationRange + row.Radius);
        }
        tacticalPoints.Seal();
        teamPoints0.Seal(); teamPoints1.Seal();
        for (var i = 0; i < rows.Length; i++) recruitmentBlocked[i] = !Recruitable(rows[i]);
        PrepareCommittedFire();
        PrepareThreatIndex();
        BuildSquads();
        for (var g = 0; g < squadCount; g++)
        {
            var squad = squadBuffers[g]; if (squad.Count < 2) continue;
            ref readonly var leader = ref rows[squad.Leader]; var brain = leader.Brain;
            var needsSync = false;
            for (var s = 0; s < squad.Count; s++) needsSync |= rows[squad.Members[s]].Brain.Target != brain.Target;
            if (brain.NextDecisionTick > Tick && TargetAvailable(leader, brain.Target) && !needsSync) continue;
            var target = SelectTarget(squad.Leader, squad, brain, out var cost);
            var changed = target != brain.Target || needsSync;
            var until = target != brain.Target ? Tick + TacticalRules.MinimumTargetLockTicks : brain.TargetLockUntilTick;
            for (var s = 0; s < squad.Count; s++)
            {
                ref var memberBrain = ref Store.Get<Brain>(rows[squad.Members[s]].Entity);
                AssignTarget(ref memberBrain, target, cost, until);
            }
            if (changed && !target.IsNone) Tactics.GroupOrders++;
        }
    }

    private int NewSquad(int leader)
    {
        ref var squad = ref squadBuffers[squadCount]; squad.Count = 0; squad.Leader = leader;
        squadLeaderPoints.Add(AiRows[leader].Transform.Position, squadCount);
        return squadCount++;
    }
    private static bool SquadEligible(in AiShipSnapshot row) => !row.Docked && row.Id.Class <= ShipClass.Gunship &&
        row.Brain.NextDecisionTick != int.MaxValue && row.Brain.Mode is BrainMode.Attack or BrainMode.Screen;
    private bool TargetAvailable(in AiShipSnapshot actor, EntityKey target)
    {
        if (!aiIndexes.TryGetValue(target, out var index)) return false;
        ref readonly var row = ref AiRows[index];
        return !row.Docked && row.Id.Team != actor.Id.Team;
    }

    private ReadOnlySpan<int> EnemyTargetIndexes(int team)
    {
        ref var count = ref (team == 0 ? ref enemyCount0 : ref enemyCount1);
        ref var indexes = ref (team == 0 ? ref enemyTargets0 : ref enemyTargets1);
        if (count < 0)
        {
            // Build only for an unlocked search; leased/no-decision phases pay no index preparation.
            var rows = AiRows;
            if (indexes.Length < rows.Length)
                Array.Resize(ref indexes, Math.Max(rows.Length, indexes.Length * 2));
            count = 0;
            for (var i = 0; i < rows.Length; i++)
            {
                ref readonly var row = ref rows[i];
                if (!row.Docked && row.Id.Team != team) indexes[count++] = i;
            }
        }
        return indexes.AsSpan(0, count);
    }

    private EntityKey SelectTarget(int actorIndex, Squad? squad, Brain brain, out float cost)
    {
        var rows = AiRows;
        ref readonly var actor = ref rows[actorIndex]; var best = float.PositiveInfinity; var target = EntityKey.None;
        var available = TargetAvailable(actor, brain.Target);
        if (available && scoreWork <= 0)
        { Tactics.BudgetFallbacks++; cost = brain.EstimatedKillSeconds; return brain.Target; }
        var current = available ? EvaluateCost(actorIndex, squad, aiIndexes[brain.Target]) : float.PositiveInfinity;
        if (float.IsFinite(current) && Tick < brain.TargetLockUntilTick)
        { Tactics.TargetRetentions++; cost = current; return brain.Target; }
        FillTargetCandidates(actorIndex);
        foreach (var i in targetCandidates)
        {
            ref readonly var row = ref rows[i];
            if (row.Docked || row.Id.Team == actor.Id.Team) continue;
            var candidate = i == (available ? aiIndexes[brain.Target] : -1) ? current : EvaluateCost(actorIndex, squad, i);
            if (candidate < best) { best = candidate; target = row.Entity; }
            if (scoreWork <= 0) break;
        }
        if (float.IsFinite(current) && (target == brain.Target || best >= current * (1 - TacticalRules.SwitchImprovement)))
        { Tactics.TargetRetentions++; cost = current; return brain.Target; }
        cost = float.IsFinite(best) ? best : 0; return target;
    }

    private void AssignTarget(ref Brain brain, EntityKey target, float cost, int until)
    {
        if (brain.Target != target && !brain.Target.IsNone) Tactics.TargetSwitches++;
        brain.Target = target; brain.TargetLockUntilTick = target.IsNone ? 0 : until;
        brain.EstimatedKillSeconds = float.IsFinite(cost) ? cost : 0;
    }

    private void DecideTactics(EntityKey e, Identity id, ref Brain brain, Transform transform)
    {
        if (Store.Has<Docked>(e) || brain.NextDecisionTick == int.MaxValue || !aiIndexes.TryGetValue(e, out var index)) return;
        ref readonly var row = ref AiRows[index];
        var invalidTarget = !brain.Target.IsNone && (!TargetAvailable(row, brain.Target) ||
            brain.SquadLeader.IsNone && scoreWork > 0 && !float.IsFinite(EvaluateCost(index, null, aiIndexes[brain.Target])));
        var invalidCover = brain.Mode == BrainMode.Screen && (!AliveShip(brain.CoverAlly) || Store.Has<Docked>(brain.CoverAlly));
        if (brain.NextDecisionTick > Tick && !invalidTarget && !invalidCover) return;
        brain.NextDecisionTick = Tick + 12 + id.Serial % 8;
        if (!AliveShip(brain.Home)) brain.Home = NearestHome(id.Team, transform.Position);
        var hull = row.Hull.Current / row.Hull.Maximum;
        var shield = row.Shield.Current / MathF.Max(1, row.Shield.Maximum);
        var critical = !ShipCatalog.IsCarrier(id.Class) && !brain.Home.IsNone &&
            (hull < 0.25f || row.Heat.Value > row.Heat.Limit * 0.92f || brain.Mode == BrainMode.Retreat && hull < 0.85f);
        if (critical) { brain.Mode = BrainMode.Retreat; brain.CoverAlly = EntityKey.None; }
        else if (brain.Mode != BrainMode.Docking)
        {
            var threat = FindThreat(index); var recovered = hull >= 0.75f && shield >= 0.35f && row.Heat.Value < row.Heat.Limit * 0.8f;
            var stay = brain.Mode == BrainMode.Screen && !recovered && (!threat.IsNone || Tick < brain.CoverUntilTick);
            var enter = !threat.IsNone && (hull < 0.65f || shield < 0.18f);
            var cover = (stay || enter) && squadIndexes.TryGetValue(brain.SquadLeader, out var gi) && squadBuffers[gi].Count >= 2
                ? FindCover(index) : EntityKey.None;
            if (!cover.IsNone)
            {
                if (brain.Mode != BrainMode.Screen || brain.CoverAlly != cover)
                { brain.CoverUntilTick = Tick + 90; Tactics.CoverOrders++; }
                brain.Mode = BrainMode.Screen; brain.CoverAlly = cover;
            }
            else { brain.Mode = BrainMode.Attack; brain.CoverAlly = EntityKey.None; }
        }
        if (brain.SquadLeader.IsNone)
        {
            var target = SelectTarget(index, null, brain, out var cost);
            AssignTarget(ref brain, target, cost, target != brain.Target ? Tick + TacticalRules.MinimumTargetLockTicks : brain.TargetLockUntilTick);
        }
        if (brain.Mode == BrainMode.Retreat && AliveShip(brain.Home))
        {
            var range = Store.Get<Collider>(brain.Home).Radius + row.Radius + 3;
            if (Vector2.DistanceSquared(transform.Position, Store.Get<Transform>(brain.Home).Position) < range * range)
                State(e, StateKind.Dock, true, brain.Home);
        }
    }

    private void PrepareThreatIndex()
    {
        if (incomingHeads.Length < aiRows.Count)
        {
            var capacity = Math.Max(aiRows.Count, incomingHeads.Length * 2);
            incomingHeads = new int[capacity]; incomingNext = new int[capacity];
            cachedThreats = new EntityKey[capacity]; threatCached = new bool[capacity];
        }
        Array.Fill(incomingHeads, -1, 0, aiRows.Count);
        Array.Clear(threatCached, 0, aiRows.Count);
        // Prepend in reverse snapshot order to preserve the original ascending tie order.
        for (var i = aiRows.Count - 1; i >= 0; i--)
        {
            ref readonly var enemy = ref AiRows[i];
            if (enemy.Docked || enemy.Brain.Mode != BrainMode.Attack || !aiIndexes.TryGetValue(enemy.Brain.Target, out var target) ||
                enemy.Id.Team == AiRows[target].Id.Team) continue;
            incomingNext[i] = incomingHeads[target]; incomingHeads[target] = i;
        }
    }

    private EntityKey FindThreat(int index)
    {
        if (threatCached[index]) return cachedThreats[index];
        var threat = FindSnapshotThreat(index);
        cachedThreats[index] = threat; threatCached[index] = true;
        return threat;
    }

    private EntityKey FindSnapshotThreat(int index)
    {
        ref readonly var row = ref AiRows[index];
        if (Tick - row.Combat.LastDamageTick <= 180 && TargetAvailable(row, row.Combat.LastAttacker))
        {
            ref readonly var attacker = ref AiRows[aiIndexes[row.Combat.LastAttacker]];
            if (Vector2.DistanceSquared(row.Transform.Position, attacker.Transform.Position) < MathF.Pow(MathF.Max(attacker.Primary.Range, attacker.Secondary.Range) * 1.3f, 2))
                return attacker.Entity;
        }
        var nearest = float.PositiveInfinity; var threat = EntityKey.None;
        for (var i = incomingHeads[index]; i >= 0; i = incomingNext[i])
        {
            ref readonly var enemy = ref AiRows[i];
            var distance = Vector2.DistanceSquared(row.Transform.Position, enemy.Transform.Position);
            var range = MathF.Max(enemy.Primary.Range, enemy.Secondary.Range) * 1.1f;
            if (distance < range * range && distance < nearest) { nearest = distance; threat = enemy.Entity; }
        }
        return threat;
    }
    private EntityKey FindCover(int index)
    {
        ref readonly var row = ref AiRows[index]; var vitality = Vitality(row); var best = float.NegativeInfinity; var cover = EntityKey.None;
        QueryTactical(MemberPoints(row.Id.Team), row.Transform.Position, coverQueryRadius, coverCandidates, Config.Tactics.MemberCandidates);
        if (squadIndexes.TryGetValue(row.Brain.SquadLeader, out var group))
        {
            var squad = squadBuffers[group];
            for (var i = 0; i < squad.Count; i++)
                if (!coverCandidates.Contains(squad.Members[i])) coverCandidates.Add(squad.Members[i]);
            coverCandidates.Sort();
        }
        foreach (var candidate in coverCandidates)
        {
            ref readonly var ally = ref AiRows[candidate];
            if (ally.Id.Team != row.Id.Team || ally.Entity == row.Entity || ally.Docked ||
                ally.Brain.Mode is BrainMode.Screen or BrainMode.Retreat or BrainMode.Docking ||
                ally.Hull.Current < ally.Hull.Maximum * 0.65f || Vitality(ally) < vitality + 0.15f) continue;
            if (ally.Heat.Value > ally.Heat.Limit * 0.92f ||
                (ally.Hull.Current < ally.Hull.Maximum * 0.65f || ally.Shield.Current < ally.Shield.Maximum * 0.18f) &&
                !FindThreat(aiIndexes[ally.Entity]).IsNone) continue;
            var distance = Vector2.Distance(row.Transform.Position, ally.Transform.Position);
            if (distance > TacticalRules.FormationRange + ally.Radius) continue;
            var sameGroup = !row.Brain.SquadLeader.IsNone && ally.Brain.SquadLeader == row.Brain.SquadLeader;
            var score = Vitality(ally) + (sameGroup ? 0.5f : 0) - distance * 0.008f;
            if (score > best) { best = score; cover = ally.Entity; }
        }
        return cover;
    }
    private static float Vitality(in AiShipSnapshot row) => row.Hull.Current / row.Hull.Maximum * 0.65f +
        row.Shield.Current / MathF.Max(1, row.Shield.Maximum) * 0.35f;

    // Steering must not read another processor's already-updated velocity.
    private Vector2 CoverVelocity(EntityKey ally) => aiIndexes.TryGetValue(ally, out var index)
        ? AiRows[index].Motion.Velocity : Vector2.Zero;

    private Vector2 ScreenDestination(EntityKey e, Brain brain)
    {
        var ally = Store.Get<Transform>(brain.CoverAlly).Position;
        var threat = aiIndexes.TryGetValue(e, out var index) ? FindThreat(index) : EntityKey.None;
        if (threat.IsNone) threat = brain.Target;
        var away = AliveShip(threat) ? Direction(ally - Store.Get<Transform>(threat).Position) : Direction(ally - Store.Get<Transform>(e).Position);
        if (away == Vector2.Zero) away = Vector2.UnitX;
        var clearance = Store.Get<Collider>(brain.CoverAlly).Radius + Store.Get<Collider>(e).Radius + 6;
        var relative = Store.Get<Transform>(e).Position - ally;
        var side = new Vector2(-away.Y, away.X);
        if (Vector2.Dot(relative, away) < 0 && MathF.Abs(Vector2.Dot(relative, side)) < clearance * 0.75f)
            return ally + side * clearance * brain.OrbitSign + away * (clearance * 0.4f);
        return ally + away * clearance;
    }
    private Vector2 FormationSteering(EntityKey e, Brain brain, Vector2 position, float maxSpeed)
    {
        if (brain.Mode != BrainMode.Attack || !squadIndexes.TryGetValue(brain.SquadLeader, out var gi) ||
            !aiIndexes.TryGetValue(brain.Target, out var targetIndex) || !aiIndexes.TryGetValue(e, out var actorIndex)) return Vector2.Zero;
        var squad = squadBuffers[gi]; var rows = AiRows; var anchor = squad.Leader; var slot = 0; var healthiest = -1f;
        for (var s = 0; s < squad.Count; s++)
        {
            ref readonly var row = ref rows[squad.Members[s]];
            if (row.Entity == e) slot = s;
            if (Store.Get<Brain>(row.Entity).Mode != BrainMode.Attack) continue;
            var vitality = Vitality(row);
            if (vitality > healthiest) { healthiest = vitality; anchor = squad.Members[s]; }
        }
        var victim = rows[targetIndex].Transform.Position;
        var axis = Direction(rows[anchor].Transform.Position - victim);
        if (axis == Vector2.Zero) axis = rows[actorIndex].Id.Team == 0 ? -Vector2.UnitX : Vector2.UnitX;
        var side = new Vector2(-axis.Y, axis.X);
        var offsetAngle = (slot - (squad.Count - 1) * 0.5f) * 0.3f;
        var lane = axis * MathF.Cos(offsetAngle) + side * MathF.Sin(offsetAngle);
        var range = GroupWorkingRange(e, brain.Target, OperatingRange(rows[actorIndex]));
        var radius = MathF.Max(rows[targetIndex].Radius + rows[actorIndex].Radius + 6, range * 0.72f);
        var destination = victim + lane * radius;
        var desired = ClampMagnitude((destination - position) * 0.7f, maxSpeed * 0.55f);
        var ray = victim - position; var rayLength = ray.LengthSquared();
        for (var s = 0; s < squad.Count; s++)
        {
            ref readonly var ally = ref rows[squad.Members[s]];
            if (ally.Entity == e) continue;
            var relative = ally.Transform.Position - position;
            var clearance = ally.Radius + rows[actorIndex].Radius + 5;
            var distance = relative.Length();
            if (distance < clearance)
                desired -= (distance > 0.01f ? relative / distance : side * (slot % 2 == 0 ? 1 : -1)) * (clearance - distance);
            if (rayLength <= 0) continue;
            var fraction = Vector2.Dot(relative, ray) / rayLength;
            if (fraction <= 0 || fraction >= 1) continue;
            var perpendicular = relative - ray * fraction;
            if (perpendicular.LengthSquared() < clearance * clearance)
                desired += side * (slot % 2 == 0 ? 1 : -1) * (maxSpeed * 0.2f);
        }
        return ClampMagnitude(desired, maxSpeed * 0.65f);
    }
}
