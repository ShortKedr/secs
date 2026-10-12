using System.Numerics;

namespace SpaceBattle;

public sealed partial class Battle<TStore>
{
    private readonly List<int> coverCandidates = new(68);
    private float maximumTargetSpeed;

    private SpatialPointIndex MemberPoints(int team) => team == 0 ? teamPoints0 : team == 1 ? teamPoints1 : tacticalPoints;
    private SpatialPointIndex OpponentPoints(int team) => team == 0 ? teamPoints1 : team == 1 ? teamPoints0 : tacticalPoints;

    private void QueryTactical(SpatialPointIndex points, Vector2 position, float radius, List<int> output, int limit,
        ReadOnlySpan<bool> excluded = default, ReadOnlySpan<bool> ineligible = default)
    {
        var before = spatialWork;
        points.QueryLimited(position, radius, output, limit, ref spatialWork, excluded, ineligible);
        Tactics.SpatialWork += before - spatialWork;
        if (spatialWork == 0) Tactics.BudgetFallbacks++;
    }

    private void FillTargetCandidates(int actorIndex)
    {
        ref readonly var actor = ref AiRows[actorIndex];
        var radius = (double)OperatingRange(actor) + Config.Tactics.MaximumEngageSeconds * ((double)actor.Motion.MaxSpeed + maximumTargetSpeed);
        QueryTactical(OpponentPoints(actor.Id.Team), actor.Transform.Position, (float)Math.Min(float.MaxValue, radius),
            targetCandidates, Config.Tactics.LocalTargetCandidates);
        var indexed = actor.Id.Team is 0 or 1;
        var enemies = indexed ? EnemyTargetIndexes(actor.Id.Team) : default;
        var count = indexed ? enemies.Length : AiRows.Length;
        if (count > 0)
        {
            var offset = ((long)actor.Id.Serial * 17 + Tick / Config.Tactics.FormationReviewTicks) % count;
            var start = (int)(offset < 0 ? offset + count : offset);
            for (var n = 0; n < Math.Min(count, Config.Tactics.GlobalTargetProbes); n++)
            {
                var index = (start + n) % count; var row = indexed ? enemies[index] : index;
                if (!targetCandidates.Contains(row) && TargetAvailable(actor, AiRows[row].Entity)) targetCandidates.Add(row);
            }
        }
        targetCandidates.Sort();
    }

    private void FillFormationTargets(int leader)
    {
        FillTargetCandidates(leader); formationTargets.Clear();
        ref readonly var actor = ref AiRows[leader];
        Span<float> arrivals = stackalloc float[16];
        if (TargetAvailable(actor, actor.Brain.Target)) formationTargets.Add(aiIndexes[actor.Brain.Target]);
        var retained = formationTargets.Count;
        foreach (var target in targetCandidates)
        {
            if (!TargetAvailable(actor, AiRows[target].Entity) || formationTargets.Contains(target)) continue;
            var arrival = MemberEngageSeconds(actor, AiRows[target]);
            if (!float.IsFinite(arrival)) continue;
            var slot = retained;
            while (slot < formationTargets.Count && arrivals[slot] <= arrival) slot++;
            if (slot >= Config.Tactics.FormationTargetCandidates) continue;
            if (formationTargets.Count < Config.Tactics.FormationTargetCandidates) formationTargets.Add(target);
            for (var i = formationTargets.Count - 1; i > slot; i--)
            { formationTargets[i] = formationTargets[i - 1]; arrivals[i] = arrivals[i - 1]; }
            formationTargets[slot] = target; arrivals[slot] = arrival;
        }
    }

    private float EvaluateCost(int actor, Squad? squad, int target)
    {
        if (scoreWork <= 0) { Tactics.BudgetFallbacks++; return float.PositiveInfinity; }
        scoreWork--; Tactics.ScoreEvaluations++;
        return TargetCost(actor, squad, target);
    }

    private void PrepareCommittedFire()
    {
        var count = Math.Min(projectiles.Count, Config.Tactics.MaximumIntentProjectiles);
        for (var i = 0; i < count; i++)
        {
            var entity = projectiles[i].Entity;
            if (!Store.Alive(entity)) continue;
            var shot = Store.Get<Projectile>(entity); Tactics.IntentProjectiles++;
            if (shot.ExpiresTick <= Tick || !aiIndexes.TryGetValue(shot.Target, out var targetIndex) ||
                !Store.Alive(shot.Owner) || !Store.Has<Identity>(shot.Owner)) continue;
            ref readonly var target = ref AiRows[targetIndex];
            if (target.Docked || Store.Get<Identity>(shot.Owner).Team == target.Id.Team) continue;
            var position = Store.Get<Transform>(entity).Position; var velocity = Store.Get<Motion>(entity).Velocity;
            var delta = target.Transform.Position - position; var distance = delta.Length(); var speed = velocity.Length();
            if (speed <= 0 || !float.IsFinite(speed + distance) || Vector2.Dot(velocity, delta) < speed * distance * 0.5f) continue;
            var flight = distance / speed;
            if (flight > Config.Tactics.MaximumEngageSeconds || flight > (shot.ExpiresTick - (double)Tick) / Config.TickRate) continue;
            var probability = Config.Tactics.IncomingHitConfidence *
                target.Radius / MathF.Max(target.Radius, target.Radius + target.Motion.Velocity.Length() * flight * 0.025f);
            var profile = DamageRules.ForWeapon(shot.Kind, Config.Damage);
            var damage = DamageRules.Resolve(shot.Damage, target.Shield.Current, target.Hull.Armor, profile);
            committedShield[targetIndex] = MathF.Min(target.Shield.Current * 0.5f, committedShield[targetIndex] + damage.Shield * probability);
            committedHull[targetIndex] = MathF.Min(target.Hull.Current * 0.5f, committedHull[targetIndex] + damage.Hull * probability);
        }
        if (projectiles.Count > count) Tactics.BudgetFallbacks++;
    }

    private float GroupWorkingRange(EntityKey entity, EntityKey target, float fallback)
    {
        if (!aiIndexes.TryGetValue(entity, out var actor) || !aiIndexes.TryGetValue(target, out var victim)) return fallback;
        if (!rangeReady[actor] || rangeTargets[actor] != target)
        {
            preferredRanges[actor] = WorkingRange(AiRows[actor], AiRows[victim]);
            rangeTargets[actor] = target; rangeReady[actor] = true;
        }
        return preferredRanges[actor] > 0 ? preferredRanges[actor] : fallback;
    }
}
