using System.Numerics;

namespace SpaceBattle;

public static class TacticalRules
{
    public const string Revision = "tactical-lock-v2";
    public const int MinimumTargetLockTicks = 120, MaxSquadSize = 4;
    public const float FormationRange = 48, SquadBreakRange = 96, SwitchImprovement = 0.35f;
}

public sealed class AiCounters
{
    public long TargetSwitches, TargetRetentions, GroupOrders, CoverOrders;
}

// Keep this value type nongeneric for Mono WASM AOT calling conventions.
internal readonly record struct AiShipSnapshot(EntityKey Entity, Identity Id, Transform Transform, Motion Motion,
    Hull Hull, Shield Shield, Heat Heat, Weapon Primary, Weapon Secondary, Brain Brain,
    CombatState Combat, float Radius, bool Docked);

public sealed partial class Battle<TStore>
{
    // Immutable pre-decision rows keep reasoning independent of the ECS query order.
    private sealed class Squad
    {
        public readonly int[] Members = new int[TacticalRules.MaxSquadSize];
        public int Count, Leader;
    }
    private readonly List<AiShipSnapshot> aiRows = new();
    private readonly Dictionary<EntityKey, int> aiIndexes = new(), squadIndexes = new();
    private readonly List<Squad> squadBuffers = new();
    private readonly HashSet<int> assignedSquadMembers = new();
    private readonly SpatialPointIndex tacticalPoints = new();
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
        aiRows.Clear(); aiIndexes.Clear(); squadIndexes.Clear(); assignedSquadMembers.Clear(); squadCount = 0;
        squadLeaderPoints.Clear();
        foreach (var ship in ships)
        {
            var e = ship.Entity;
            if (!AliveShip(e)) continue;
            aiIndexes.Add(e, aiRows.Count);
            aiRows.Add(new(e, ship.Identity, ship.Transform, Store.Get<Motion>(e), Store.Get<Hull>(e),
                Store.Get<Shield>(e), Store.Get<Heat>(e), Store.Get<Weapon>(e),
                Store.Has<SecondaryWeapon>(e) ? Store.Get<SecondaryWeapon>(e).Value : default,
                Store.Get<Brain>(e), Store.Get<CombatState>(e), Store.Get<Collider>(e).Radius, Store.Has<Docked>(e)));
        }
        tacticalPoints.Clear(); coverQueryRadius = TacticalRules.FormationRange;
        for (var i = 0; i < aiRows.Count; i++)
        {
            tacticalPoints.Add(aiRows[i].Transform.Position, i);
            coverQueryRadius = MathF.Max(coverQueryRadius, TacticalRules.FormationRange + aiRows[i].Radius);
        }
        var repair = false;
        for (var i = 0; i < aiRows.Count; i++)
        {
            var row = aiRows[i]; var leader = row.Brain.SquadLeader;
            if (leader.IsNone) continue;
            if (!SquadEligible(row) || !aiIndexes.TryGetValue(leader, out var li) || !SquadEligible(aiRows[li]) ||
                aiRows[li].Id.Team != row.Id.Team || aiRows[li].Brain.SquadLeader != leader ||
                Vector2.DistanceSquared(row.Transform.Position, aiRows[li].Transform.Position) > TacticalRules.SquadBreakRange * TacticalRules.SquadBreakRange)
            { repair = true; continue; }
            if (!squadIndexes.TryGetValue(leader, out var gi))
            {
                gi = NewSquad(li); squadIndexes.Add(leader, gi);
                squadBuffers[gi].Members[squadBuffers[gi].Count++] = li;
                assignedSquadMembers.Add(li);
            }
            var squad = squadBuffers[gi];
            if (i != li && squad.Count < TacticalRules.MaxSquadSize && assignedSquadMembers.Add(i))
                squad.Members[squad.Count++] = i;
        }
        for (var g = 0; g < squadCount; g++)
        {
            var squad = squadBuffers[g];
            if (squad.Count >= 2) continue;
            assignedSquadMembers.Remove(squad.Leader); squad.Count = 0;
            squadIndexes.Remove(aiRows[squad.Leader].Entity); repair = true;
        }
        if (Tick >= nextFormationTick || repair)
        {
            nextFormationTick = Tick + TacticalRules.MinimumTargetLockTicks;
            for (var i = 0; i < aiRows.Count; i++)
            {
                if (!SquadEligible(aiRows[i]) || assignedSquadMembers.Contains(i)) continue;
                var nearest = -1; var best = TacticalRules.FormationRange * TacticalRules.FormationRange;
                squadLeaderPoints.Query(aiRows[i].Transform.Position, TacticalRules.FormationRange, squadLeaderCandidates);
                foreach (var g in squadLeaderCandidates)
                {
                    var squad = squadBuffers[g];
                    if (squad.Count < 2 || squad.Count >= TacticalRules.MaxSquadSize || aiRows[squad.Leader].Id.Team != aiRows[i].Id.Team) continue;
                    var d = Vector2.DistanceSquared(aiRows[i].Transform.Position, aiRows[squad.Leader].Transform.Position);
                    if (d < best) { best = d; nearest = g; }
                }
                if (nearest >= 0)
                {
                    var squad = squadBuffers[nearest]; squad.Members[squad.Count++] = i; assignedSquadMembers.Add(i); continue;
                }
                var newGroup = NewSquad(i); var fresh = squadBuffers[newGroup]; fresh.Members[fresh.Count++] = i;
                tacticalPoints.Query(aiRows[i].Transform.Position, TacticalRules.FormationRange, tacticalCandidates);
                for (var slot = 1; slot < TacticalRules.MaxSquadSize; slot++)
                {
                    nearest = -1; best = TacticalRules.FormationRange * TacticalRules.FormationRange;
                    foreach (var j in tacticalCandidates)
                    {
                        if (j <= i) continue;
                        if (!SquadEligible(aiRows[j]) || aiRows[j].Id.Team != aiRows[i].Id.Team || assignedSquadMembers.Contains(j)) continue;
                        var d = Vector2.DistanceSquared(aiRows[i].Transform.Position, aiRows[j].Transform.Position);
                        if (d < best) { best = d; nearest = j; }
                    }
                    if (nearest < 0) break;
                    fresh.Members[fresh.Count++] = nearest; assignedSquadMembers.Add(nearest);
                }
                if (fresh.Count < 2) { fresh.Count = 0; continue; }
                assignedSquadMembers.Add(i); squadIndexes.Add(aiRows[i].Entity, newGroup);
            }
        }
        for (var i = 0; i < aiRows.Count; i++) Store.Get<Brain>(aiRows[i].Entity).SquadLeader = EntityKey.None;
        for (var g = 0; g < squadCount; g++)
        {
            var squad = squadBuffers[g]; if (squad.Count < 2) continue;
            var leader = aiRows[squad.Leader].Entity;
            for (var s = 0; s < squad.Count; s++) Store.Get<Brain>(aiRows[squad.Members[s]].Entity).SquadLeader = leader;
        }
        for (var i = 0; i < aiRows.Count; i++)
            aiRows[i] = aiRows[i] with { Brain = Store.Get<Brain>(aiRows[i].Entity) };
        PrepareThreatIndex();
        for (var g = 0; g < squadCount; g++)
        {
            var squad = squadBuffers[g]; if (squad.Count < 2) continue;
            var leader = aiRows[squad.Leader]; var brain = leader.Brain;
            var needsSync = false;
            for (var s = 0; s < squad.Count; s++) needsSync |= aiRows[squad.Members[s]].Brain.Target != brain.Target;
            if (brain.NextDecisionTick > Tick && (brain.Target.IsNone || TargetAvailable(leader, brain.Target) &&
                float.IsFinite(TargetCost(squad.Leader, squad, aiIndexes[brain.Target]))) && !needsSync) continue;
            var target = SelectTarget(squad.Leader, squad, brain, out var cost);
            var changed = target != brain.Target || needsSync;
            var until = target != brain.Target ? Tick + TacticalRules.MinimumTargetLockTicks : brain.TargetLockUntilTick;
            for (var s = 0; s < squad.Count; s++)
            {
                ref var memberBrain = ref Store.Get<Brain>(aiRows[squad.Members[s]].Entity);
                AssignTarget(ref memberBrain, target, cost, until);
            }
            if (changed && !target.IsNone) Tactics.GroupOrders++;
        }
    }

    private int NewSquad(int leader)
    {
        if (squadCount == squadBuffers.Count) squadBuffers.Add(new());
        var squad = squadBuffers[squadCount]; squad.Count = 0; squad.Leader = leader;
        squadLeaderPoints.Add(aiRows[leader].Transform.Position, squadCount);
        return squadCount++;
    }
    private static bool SquadEligible(AiShipSnapshot row) => !row.Docked && row.Id.Class <= ShipClass.Gunship &&
        row.Brain.NextDecisionTick != int.MaxValue && row.Brain.Mode is BrainMode.Attack or BrainMode.Screen;
    private bool TargetAvailable(AiShipSnapshot actor, EntityKey target) => aiIndexes.TryGetValue(target, out var index) &&
        !aiRows[index].Docked && aiRows[index].Id.Team != actor.Id.Team;

    private EntityKey SelectTarget(int actorIndex, Squad? squad, Brain brain, out float cost)
    {
        var actor = aiRows[actorIndex]; var best = float.PositiveInfinity; var target = EntityKey.None;
        var current = TargetAvailable(actor, brain.Target) ? TargetCost(actorIndex, squad, aiIndexes[brain.Target]) : float.PositiveInfinity;
        if (float.IsFinite(current) && Tick < brain.TargetLockUntilTick)
        { Tactics.TargetRetentions++; cost = current; return brain.Target; }
        for (var i = 0; i < aiRows.Count; i++)
        {
            if (aiRows[i].Docked || aiRows[i].Id.Team == actor.Id.Team) continue;
            var candidate = TargetCost(actorIndex, squad, i);
            if (candidate < best) { best = candidate; target = aiRows[i].Entity; }
        }
        if (float.IsFinite(current) && (target == brain.Target || best >= current * (1 - TacticalRules.SwitchImprovement)))
        { Tactics.TargetRetentions++; cost = current; return brain.Target; }
        cost = float.IsFinite(best) ? best : 0; return target;
    }

    private float TargetCost(int actorIndex, Squad? squad, int targetIndex)
    {
        var target = aiRows[targetIndex]; float dps = 0, approach = float.PositiveInfinity, flight = 0;
        var count = squad?.Count ?? 1; var threatening = false;
        for (var s = 0; s < count; s++)
        {
            var actor = aiRows[squad is null ? actorIndex : squad.Members[s]];
            if (count > 1 && actor.Brain.Mode != BrainMode.Attack) continue;
            var distance = Vector2.Distance(actor.Transform.Position, target.Transform.Position);
            var range = MathF.Max(actor.Primary.Range, actor.Secondary.Range);
            var excess = MathF.Max(0, distance - range * 0.9f);
            var radialSpeed = Vector2.Dot(target.Motion.Velocity, Direction(target.Transform.Position - actor.Transform.Position));
            var closingSpeed = actor.Motion.MaxSpeed - radialSpeed;
            if (distance > range && (closingSpeed <= 0.5f || ShipCatalog.IsCarrier(actor.Id.Class) && distance > range * 1.5f)) continue;
            var engage = distance <= range ? 0 : excess / MathF.Max(1, closingSpeed);
            if (engage > 15) continue;
            var primary = ExpectedDps(actor, target, actor.Primary, distance, out var firstFlight);
            var secondary = ExpectedDps(actor, target, actor.Secondary, distance, out var secondFlight);
            var heatRate = WeaponHeatRate(actor.Primary) + WeaponHeatRate(actor.Secondary);
            var heatBudget = actor.Heat.CoolingPerSecond + MathF.Max(0, actor.Heat.Limit - actor.Heat.Value) / 3;
            var contribution = (primary + secondary) * Math.Clamp(heatBudget / MathF.Max(0.01f, heatRate), 0.15f, 1);
            var mountCount = ShipCatalog.TurretCount(actor.Id.Class);
            if (mountCount > 0)
            {
                // Mounts have independent heat budgets; do not throttle them by the main cannon's heat.
                var cannon = ShipCatalog.TurretWeapon(actor.Id.Class, 0);
                var plasma = ShipCatalog.TurretWeapon(actor.Id.Class, 1);
                var cannonDps = ExpectedDps(actor, target, cannon, distance, out var cannonFlight);
                var plasmaDps = ExpectedDps(actor, target, plasma, distance, out var plasmaFlight);
                contribution += distance > cannon.Range ? 0 : mountCount / 2 * (cannonDps *
                    Math.Clamp(12 / MathF.Max(0.01f, WeaponHeatRate(cannon)), 0, 1) +
                    plasmaDps *
                    Math.Clamp(12 / MathF.Max(0.01f, WeaponHeatRate(plasma)), 0, 1));
                firstFlight = MathF.Max(firstFlight, MathF.Max(cannonFlight, plasmaFlight));
            }
            if (contribution <= 0) continue;
            dps += contribution; approach = MathF.Min(approach, engage); flight = MathF.Max(flight, MathF.Max(firstFlight, secondFlight));
            threatening |= actor.Combat.LastAttacker == target.Entity && Tick - actor.Combat.LastDamageTick <= 180 ||
                target.Brain.Target == actor.Entity && target.Brain.Mode == BrainMode.Attack;
        }
        if (dps <= 0) return float.PositiveInfinity;
        var effectiveHealth = target.Shield.Current + target.Hull.Current / MathF.Max(0.05f, 1 - target.Hull.Armor);
        return (effectiveHealth / dps + approach * 1.8f + flight * 0.5f) * (threatening ? 0.82f : 1);
    }
    private float ExpectedDps(AiShipSnapshot actor, AiShipSnapshot target, Weapon weapon, float distance, out float flight)
    {
        flight = 0; if (weapon.Cooldown <= 0 || weapon.Damage <= 0 || weapon.Speed <= 0) return 0;
        var speed = weapon.Kind == WeaponKind.Missile ? 85 : weapon.Speed;
        var aim = FireControl.Predict(actor.Transform.Position, actor.Motion.Velocity, target.Transform.Position,
            target.Motion.Velocity, speed, actor.Radius + 0.9f);
        if (distance > actor.Radius + target.Radius + 0.9f && (aim.InterceptTime <= 0 || aim.InterceptTime > 4)) return 0;
        flight = aim.InterceptTime;
        var accuracy = Math.Clamp(target.Radius / (target.Radius + target.Motion.Velocity.Length() * flight * 0.025f), 0.25f, 1);
        var rangeWeight = Math.Clamp(weapon.Range / MathF.Max(weapon.Range, distance), 0, 1);
        return weapon.Damage * weapon.Burst * Config.TickRate * Config.ChurnMultiplier / weapon.Cooldown * accuracy * rangeWeight;
    }
    private float WeaponHeatRate(Weapon weapon) => weapon.Cooldown <= 0 ? 0 :
        weapon.HeatPerShot * weapon.Burst * Config.TickRate * Config.ChurnMultiplier / weapon.Cooldown;
    private void AssignTarget(ref Brain brain, EntityKey target, float cost, int until)
    {
        if (brain.Target != target && !brain.Target.IsNone) Tactics.TargetSwitches++;
        brain.Target = target; brain.TargetLockUntilTick = target.IsNone ? 0 : until;
        brain.EstimatedKillSeconds = float.IsFinite(cost) ? cost : 0;
    }

    private void DecideTactics(EntityKey e, Identity id, ref Brain brain, Transform transform)
    {
        if (Store.Has<Docked>(e) || brain.NextDecisionTick == int.MaxValue || !aiIndexes.TryGetValue(e, out var index)) return;
        var row = aiRows[index];
        var invalidTarget = !brain.Target.IsNone && (!TargetAvailable(row, brain.Target) ||
            brain.SquadLeader.IsNone && !float.IsFinite(TargetCost(index, null, aiIndexes[brain.Target])));
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
            var enemy = aiRows[i];
            if (enemy.Docked || enemy.Brain.Mode != BrainMode.Attack || !aiIndexes.TryGetValue(enemy.Brain.Target, out var target) ||
                enemy.Id.Team == aiRows[target].Id.Team) continue;
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
        var row = aiRows[index];
        if (Tick - row.Combat.LastDamageTick <= 180 && TargetAvailable(row, row.Combat.LastAttacker))
        {
            var attacker = aiRows[aiIndexes[row.Combat.LastAttacker]];
            if (Vector2.DistanceSquared(row.Transform.Position, attacker.Transform.Position) < MathF.Pow(MathF.Max(attacker.Primary.Range, attacker.Secondary.Range) * 1.3f, 2))
                return attacker.Entity;
        }
        var nearest = float.PositiveInfinity; var threat = EntityKey.None;
        for (var i = incomingHeads[index]; i >= 0; i = incomingNext[i])
        {
            var enemy = aiRows[i];
            var distance = Vector2.DistanceSquared(row.Transform.Position, enemy.Transform.Position);
            var range = MathF.Max(enemy.Primary.Range, enemy.Secondary.Range) * 1.1f;
            if (distance < range * range && distance < nearest) { nearest = distance; threat = enemy.Entity; }
        }
        return threat;
    }
    private EntityKey FindCover(int index)
    {
        var row = aiRows[index]; var vitality = Vitality(row); var best = float.NegativeInfinity; var cover = EntityKey.None;
        tacticalPoints.Query(row.Transform.Position, coverQueryRadius, tacticalCandidates);
        foreach (var candidate in tacticalCandidates)
        {
            var ally = aiRows[candidate];
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
    private static float Vitality(AiShipSnapshot row) => row.Hull.Current / row.Hull.Maximum * 0.65f +
        row.Shield.Current / MathF.Max(1, row.Shield.Maximum) * 0.35f;

    // Steering must not read another processor's already-updated velocity.
    private Vector2 CoverVelocity(EntityKey ally) => aiIndexes.TryGetValue(ally, out var index)
        ? aiRows[index].Motion.Velocity : Vector2.Zero;

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
        if (brain.Mode != BrainMode.Attack || !squadIndexes.TryGetValue(brain.SquadLeader, out var gi)) return Vector2.Zero;
        var squad = squadBuffers[gi]; var anchor = -1; var slot = 0; var healthiest = -1f;
        for (var s = 0; s < squad.Count; s++)
        {
            var row = aiRows[squad.Members[s]];
            if (row.Entity == e) slot = s;
            if (Store.Get<Brain>(row.Entity).Mode != BrainMode.Attack) continue;
            var vitality = Vitality(row);
            if (vitality > healthiest) { healthiest = vitality; anchor = squad.Members[s]; }
        }
        if (anchor < 0 || aiRows[anchor].Entity == e) return Vector2.Zero;
        var destination = aiRows[anchor].Transform.Position + new Vector2(0, (slot - 1.5f) * 8);
        var delta = destination - position;
        return delta.LengthSquared() < 20 * 20 ? Vector2.Zero : ClampMagnitude(delta * 0.35f, maxSpeed * 0.4f);
    }
}
