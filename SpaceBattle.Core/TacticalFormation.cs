using System.Numerics;

namespace SpaceBattle;

public sealed partial class Battle<TStore>
{
    private void BuildSquads()
    {
        var rows = AiRows; var repair = false;
        for (var i = 0; i < rows.Length; i++)
        {
            ref readonly var row = ref rows[i]; var previous = row.Brain.SquadLeader;
            if (previous.IsNone || assignedSquadMembers[i]) continue;
            if (!SquadEligible(row)) { repair = true; continue; }
            var leader = i;
            var existing = squadIndexes.TryGetValue(previous, out var gi);
            if (existing) leader = squadBuffers[gi].Leader;
            else if (aiIndexes.TryGetValue(previous, out var li) && SquadEligible(rows[li]) &&
                rows[li].Id.Team == row.Id.Team && rows[li].Brain.SquadLeader == previous && !assignedSquadMembers[li])
                leader = li;
            else repair = true; // The first surviving serial becomes leader, retaining the remaining roster.
            if (rows[leader].Id.Team != row.Id.Team) { repair = true; continue; }
            if (Vector2.DistanceSquared(row.Transform.Position, rows[leader].Transform.Position) > TacticalRules.SquadBreakRange * TacticalRules.SquadBreakRange)
            { repair = true; continue; }
            if (!existing)
            {
                gi = NewSquad(leader); squadIndexes.Add(previous, gi);
                squadBuffers[gi].Members[squadBuffers[gi].Count++] = leader;
                assignedSquadMembers[leader] = true;
            }
            ref var squad = ref squadBuffers[gi];
            if (i != squad.Leader && squad.Count < TacticalRules.MaxSquadSize)
            { squad.Members[squad.Count++] = i; assignedSquadMembers[i] = true; }
        }
        for (var g = 0; g < squadCount; g++)
        {
            ref var squad = ref squadBuffers[g];
            if (squad.Count < rows[squad.Leader].Brain.SquadSize) repair = true;
            if (!rows[squad.Leader].Brain.Target.IsNone && !TargetAvailable(rows[squad.Leader], rows[squad.Leader].Brain.Target)) repair = true;
            if (squad.Count >= 2) continue;
            assignedSquadMembers[squad.Leader] = false; squad.Count = 0; repair = true;
        }
        if (Tick >= nextFormationTick || repair)
        {
            nextFormationTick = Tick + Config.Tactics.FormationReviewTicks;
            Tactics.FormationReviews++;
            for (var g = 0; g < squadCount; g++)
            {
                ref var squad = ref squadBuffers[g];
                if (squad.Count < 2) continue;
                for (var s = squad.Count - 1; s >= 1; s--)
                    if (!CompatibleMembers(squad.Leader, squad.Members[s], -1))
                    {
                        assignedSquadMembers[squad.Members[s]] = false;
                        RemoveMember(ref squad, s);
                    }
                if (squad.Count < 2) { assignedSquadMembers[squad.Leader] = false; squad.Count = 0; continue; }
                RecruitMembers(ref squad, true);
            }
            for (var i = 0; i < rows.Length; i++)
            {
                if (assignedSquadMembers[i] || !Recruitable(rows[i])) continue;
                var gi = NewSquad(i); ref var fresh = ref squadBuffers[gi];
                fresh.Members[fresh.Count++] = i; assignedSquadMembers[i] = true;
                RecruitMembers(ref fresh, false);
                if (fresh.Count < 2) { fresh.Count = 0; assignedSquadMembers[i] = false; }
            }
        }
        squadIndexes.Clear();
        for (var i = 0; i < rows.Length; i++)
        { Store.Get<Brain>(rows[i].Entity).SquadLeader = EntityKey.None; Store.Get<Brain>(rows[i].Entity).SquadSize = 0; }
        for (var g = 0; g < squadCount; g++)
        {
            var squad = squadBuffers[g]; if (squad.Count < 2) continue;
            var leader = rows[squad.Leader].Entity; squadIndexes.Add(leader, g);
            for (var s = 0; s < squad.Count; s++)
            {
                ref var brain = ref Store.Get<Brain>(rows[squad.Members[s]].Entity);
                brain.SquadLeader = leader; brain.SquadSize = squad.Count;
            }
        }
        for (var i = 0; i < rows.Length; i++)
        {
            var brain = Store.Get<Brain>(rows[i].Entity);
            if (brain.SquadLeader != rows[i].Brain.SquadLeader) Tactics.GroupChanges++;
            rows[i] = rows[i] with { Brain = brain };
        }
    }

    private bool Recruitable(in AiShipSnapshot row) => SquadEligible(row) && row.Brain.Mode == BrainMode.Attack &&
        Vitality(row) >= 0.3f && ReadyDelay(row) <= Config.Tactics.MaximumJoinDelaySeconds;

    private float ReadyDelay(in AiShipSnapshot row)
    {
        var ready = Math.Min(row.Primary.NextFireTick, row.Secondary.Cooldown > 0 ? row.Secondary.NextFireTick : int.MaxValue);
        var delay = (float)Math.Max(0, (ready - (double)Tick) / Config.TickRate);
        if (!row.Overheated && row.Heat.Value < row.Heat.Limit) return delay;
        var cool = row.Heat.CoolingPerSecond > 0 ? MathF.Max(0, row.Heat.Value - row.Heat.Limit * 0.45f) / row.Heat.CoolingPerSecond : float.PositiveInfinity;
        return MathF.Max(delay, cool);
    }

    private static float OperatingRange(in AiShipSnapshot row) =>
        MathF.Max(row.Primary.Cooldown > 0 && row.Primary.Damage > 0 && row.Primary.NextFireTick != int.MaxValue ? row.Primary.Range : 0,
            row.Secondary.Cooldown > 0 && row.Secondary.Damage > 0 && row.Secondary.NextFireTick != int.MaxValue ? row.Secondary.Range : 0);

    private bool CompatibleMembers(int leader, int member, int target)
    {
        Tactics.CompatibilityChecks++;
        ref readonly var a = ref AiRows[leader]; ref readonly var b = ref AiRows[member];
        if (a.Id.Team != b.Id.Team || !SquadEligible(b)) return false;
        if (a.Brain.Mode != BrainMode.Screen && ReadyDelay(a) > Config.Tactics.MaximumEngageSeconds ||
            b.Brain.Mode != BrainMode.Screen && ReadyDelay(b) > Config.Tactics.MaximumEngageSeconds) return false;
        var speed = MathF.Max(a.Motion.MaxSpeed, b.Motion.MaxSpeed);
        var range = MathF.Max(OperatingRange(a), OperatingRange(b));
        if (speed <= 0 || range <= 0 || MathF.Min(a.Motion.MaxSpeed, b.Motion.MaxSpeed) < speed * Config.Tactics.MinimumSpeedRatio ||
            MathF.Min(OperatingRange(a), OperatingRange(b)) < range * Config.Tactics.MinimumRangeRatio) return false;
        if (target < 0) return true;
        ref readonly var victim = ref AiRows[target];
        var first = MemberEngageSeconds(a, victim); var second = MemberEngageSeconds(b, victim);
        return float.IsFinite(first) && float.IsFinite(second) &&
            MathF.Abs(first - second) <= Config.Tactics.MaximumJoinDelaySeconds;
    }

    private void RecruitMembers(ref Squad squad, bool retained)
    {
        var rows = AiRows;
        if (squad.Count == TacticalRules.MaxSquadSize && formationWork <= 0) return;
        QueryTactical(MemberPoints(rows[squad.Leader].Id.Team), rows[squad.Leader].Transform.Position,
            TacticalRules.FormationRange, formationMembers, Config.Tactics.MemberCandidates, assignedSquadMembers, recruitmentBlocked);
        if (formationMembers.Count == 0 && spatialWork == 0)
        {
            // Constant-sized serial window, not a full all-ship search, makes dense fallback progress.
            var count = Math.Min(rows.Length - 1, Config.Tactics.MemberCandidates);
            for (var n = 1; n <= count; n++)
            {
                var index = (squad.Leader + n) % rows.Length;
                if (!assignedSquadMembers[index] && !recruitmentBlocked[index]) formationMembers.Add(index);
            }
            formationMembers.Sort();
        }
        FillFormationTargets(squad.Leader);
        while (squad.Count < TacticalRules.MaxSquadSize)
        {
            var best = float.PositiveInfinity; var chosen = -1;
            foreach (var member in formationMembers)
            {
                if (assignedSquadMembers[member] || !Recruitable(rows[member]) ||
                    Vector2.DistanceSquared(rows[squad.Leader].Transform.Position, rows[member].Transform.Position) >= TacticalRules.FormationRange * TacticalRules.FormationRange ||
                    !CompatibleMembers(squad.Leader, member, -1)) continue;
                if (formationWork <= 0)
                {
                    // Stable fallback: retain groups; fill free slots with the first compatible serial.
                    Tactics.BudgetFallbacks++; chosen = member; break;
                }
                var trial = squad; trial.Members[trial.Count++] = member;
                var value = FormationCost(trial, member);
                if (value < best) { best = value; chosen = member; }
            }
            if (chosen < 0) break;
            squad.Members[squad.Count++] = chosen; assignedSquadMembers[chosen] = true;
        }
        if (!retained || squad.Count < 2 || formationWork <= 0) return;
        var current = FormationCost(squad, -1);
        var upgrade = current * (1 - Config.Tactics.RegroupImprovement); var replacement = -1; var slot = -1;
        foreach (var member in formationMembers)
        {
            if (assignedSquadMembers[member] || !Recruitable(rows[member]) || !CompatibleMembers(squad.Leader, member, -1)) continue;
            if (Vector2.DistanceSquared(rows[squad.Leader].Transform.Position, rows[member].Transform.Position) >= TacticalRules.FormationRange * TacticalRules.FormationRange) continue;
            for (var s = 1; s < squad.Count && formationWork > 0; s++)
            {
                if (rows[squad.Members[s]].Brain.Mode == BrainMode.Screen) continue;
                var trial = squad; trial.Members[s] = member;
                var value = FormationCost(trial, member);
                if (value < upgrade) { upgrade = value; replacement = member; slot = s; }
            }
        }
        if (replacement >= 0)
        { assignedSquadMembers[squad.Members[slot]] = false; assignedSquadMembers[replacement] = true; squad.Members[slot] = replacement; }
    }

    private float FormationCost(Squad squad, int candidate)
    {
        var best = float.PositiveInfinity;
        foreach (var target in formationTargets)
        {
            if (formationWork <= 0) break;
            formationWork--;
            if (candidate >= 0 && !CompatibleMembers(squad.Leader, candidate, target)) continue;
            var value = EvaluateCost(squad.Leader, squad, target);
            if (value < best) best = value;
        }
        return best;
    }

    private static void RemoveMember(ref Squad squad, int slot)
    {
        for (var i = slot + 1; i < squad.Count; i++) squad.Members[i - 1] = squad.Members[i];
        squad.Count--;
    }
}
