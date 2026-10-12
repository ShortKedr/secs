using System.Numerics;

namespace SpaceBattle;

internal readonly record struct TacticalEstimate(float KillSeconds, float Suppression, float ExpectedLosses, float Cost)
{
    public static readonly TacticalEstimate Unavailable = new(float.PositiveInfinity, 0, 0, float.PositiveInfinity);
}

internal struct TacticalFireStream
{
    public int Member;
    public float Arrival, Volley, Rate;
    public DamageProfile Profile;
}

public sealed partial class Battle<TStore>
{
    private float TargetCost(int actorIndex, Squad? squad, int targetIndex) =>
        EstimateTarget(actorIndex, squad, targetIndex).Cost;

    private TacticalEstimate EstimateTarget(int actorIndex, Squad? squad, int targetIndex)
    {
        var rows = AiRows;
        ref readonly var target = ref rows[targetIndex];
        Span<TacticalFireStream> streams = stackalloc TacticalFireStream[TacticalRules.MaxSquadSize * 2 + 2];
        var count = squad?.Count ?? 1;
        if (count < 1 || count > TacticalRules.MaxSquadSize) return TacticalEstimate.Unavailable;
        Span<int> members = stackalloc int[TacticalRules.MaxSquadSize];
        for (var s = 0; s < count; s++) members[s] = squad is null ? actorIndex : squad.Value.Members[s];
        for (var i = 1; i < count; i++)
        {
            var member = members[i]; var j = i;
            while (j > 0 && members[j - 1] > member) { members[j] = members[j - 1]; j--; }
            members[j] = member;
        }
        var streamCount = 0;
        for (var s = 0; s < count; s++)
        {
            var member = members[s];
            ref readonly var actor = ref rows[member];
            if (actor.Docked || count > 1 && actor.Brain.Mode != BrainMode.Attack) continue;
            AddMemberStreams(member, actor, target, targetIndex, count == 1, streams, ref streamCount);
        }
        if (streamCount == 0) return TacticalEstimate.Unavailable;

        // Stable insertion order breaks simultaneous impact ties by member/weapon snapshot order.
        for (var i = 1; i < streamCount; i++)
        {
            var item = streams[i]; var j = i;
            while (j > 0 && (streams[j - 1].Arrival > item.Arrival ||
                streams[j - 1].Arrival == item.Arrival && streams[j - 1].Member > item.Member))
            { streams[j] = streams[j - 1]; j--; }
            streams[j] = item;
        }
        var first = streams[0].Arrival;
        var cutoff = count > 1 ? first + Config.Tactics.MaximumJoinDelaySeconds : float.PositiveInfinity;
        while (streamCount > 0 && streams[streamCount - 1].Arrival > cutoff) streamCount--;

        // Preparation already confidence-weights commitments. Hull is already armored; never discount twice.
        var shield = DiscountCommitment(target.Shield.Current, targetIndex < committedShield.Length ? committedShield[targetIndex] : 0);
        var hull = DiscountCommitment(target.Hull.Current, targetIndex < committedHull.Length ? committedHull[targetIndex] : 0);
        float shieldRate = 0, hullRate = 0, heatRate = 0, deliveredHeat = 0, time = 0;
        var resistance = MathF.Max(0, 1 - target.Hull.Armor);
        var horizon = first + Config.Tactics.ScoringHorizonSeconds;
        var kill = float.PositiveInfinity;
        for (var i = 0; i < streamCount; i++)
        {
            ref readonly var stream = ref streams[i];
            if (stream.Arrival > horizon) break;
            if (AdvanceLayers(stream.Arrival - time, shieldRate, hullRate * resistance, heatRate,
                ref shield, ref hull, ref deliveredHeat, out var elapsed))
            { kill = time + elapsed; break; }
            time = stream.Arrival;
            var damage = DamageRules.Resolve(stream.Volley, shield, target.Hull.Armor, stream.Profile);
            shield = MathF.Max(0, shield - damage.Shield);
            hull = MathF.Max(0, hull - damage.Hull);
            deliveredHeat += damage.Shield * stream.Profile.ShieldHeatPerDamage;
            if (shield <= 0 && hull <= 0) { kill = time; break; }
            shieldRate += stream.Rate * stream.Profile.ShieldMultiplier;
            hullRate += stream.Rate * stream.Profile.HullMultiplier;
            heatRate += stream.Rate * stream.Profile.ShieldMultiplier * stream.Profile.ShieldHeatPerDamage;
        }
        if (!float.IsFinite(kill))
        {
            if (AdvanceLayers(horizon - time, shieldRate, hullRate * resistance, heatRate,
                ref shield, ref hull, ref deliveredHeat, out var elapsed))
                kill = time + elapsed;
            else
            {
                // Bounded first-impact model, then a sustainable-rate continuation, never a late-member sum.
                var tail = DamageRules.EstimateKillSeconds(shield, hull, target.Hull.Armor, shieldRate, hullRate);
                kill = horizon + tail;
            }
        }
        if (!float.IsFinite(kill)) return TacticalEstimate.Unavailable;

        var activeMembers = 0;
        for (var s = 0; s < count; s++)
        {
            var member = members[s];
            for (var i = 0; i < streamCount; i++)
                if (streams[i].Member == member && streams[i].Arrival <= MathF.Min(horizon, kill))
                { activeMembers++; break; }
        }
        float losses = 0, targetReady = float.PositiveInfinity;
        for (var s = 0; s < count; s++)
        {
            var member = members[s];
            var participates = false; var arrival = float.PositiveInfinity;
            for (var i = 0; i < streamCount; i++)
                if (streams[i].Member == member && streams[i].Arrival <= MathF.Min(horizon, kill))
                { participates = true; arrival = MathF.Min(arrival, streams[i].Arrival); }
            if (!participates) continue;
            ref readonly var actor = ref rows[member];
            targetReady = MathF.Min(targetReady, MemberEngageSeconds(target, actor));
            if (target.Brain.Mode != BrainMode.Attack) continue;
            var exposure = MathF.Min(Config.Tactics.ScoringHorizonSeconds, MathF.Max(0, kill - arrival));
            var focused = target.Brain.Target == actor.Entity ||
                actor.Combat.LastAttacker == target.Entity && Tick - actor.Combat.LastDamageTick <= 180;
            losses += CounterLoss(target, actor, exposure) * (focused ? 1 : 1f / Math.Max(1, activeMembers));
        }
        var exposureWindow = MathF.Max(1f / Config.TickRate, MathF.Min(kill, Config.Tactics.ScoringHorizonSeconds));
        var heatBudget = MathF.Max(0.01f, target.Heat.Limit - target.Heat.Value +
            target.Heat.CoolingPerSecond * exposureWindow);
        var opportunity = target.Brain.Mode == BrainMode.Attack && float.IsFinite(targetReady) && !target.Overheated
            ? Math.Clamp((exposureWindow - targetReady) / exposureWindow, 0, 1) : 0;
        var suppression = Config.Tactics.MaximumSuppressionBenefit *
            Math.Clamp(deliveredHeat / heatBudget, 0, 1) * opportunity;
        var cost = kill * (1 + Config.Tactics.ThreatWeight * losses) * (1 - suppression);
        return new(kill, suppression, losses, cost);
    }

    private static float DiscountCommitment(float current, float pending) => MathF.Max(0, current) -
        Math.Clamp(pending, 0, MathF.Max(0, current) * 0.5f);

    private static bool AdvanceLayers(float duration, float shieldRate, float armoredHullRate, float heatRate,
        ref float shield, ref float hull, ref float heat, out float elapsed)
    {
        elapsed = 0;
        if (shield <= 0 && hull <= 0) return true;
        if (duration <= 0) return false;
        if (shield > 0)
        {
            if (!(shieldRate > 0)) return false;
            var shieldTime = shield / shieldRate;
            var spent = MathF.Min(duration, shieldTime);
            var damage = MathF.Min(shield, shieldRate * spent);
            shield = MathF.Max(0, shield - damage); heat += damage * heatRate / shieldRate;
            elapsed += spent; duration -= spent;
            if (shieldTime > spent) return false;
            shield = 0;
        }
        if (hull <= 0) return true;
        if (!(armoredHullRate > 0)) return false;
        var hullTime = hull / armoredHullRate;
        if (hullTime <= duration) { elapsed += hullTime; hull = 0; return true; }
        hull = MathF.Max(0, hull - armoredHullRate * duration);
        return false;
    }

    private static bool ScoringWeaponAvailable(in Weapon weapon) => weapon.NextFireTick != int.MaxValue &&
        weapon.Cooldown > 0 && weapon.Burst > 0 && weapon.Damage > 0 && float.IsFinite(weapon.Damage) &&
        weapon.Speed > 0 && float.IsFinite(weapon.Speed) && weapon.Range > 0 && float.IsFinite(weapon.Range);

    // Formation helpers reason only from the immutable phase rows, never live component refs.
    private float WorkingRange(in AiShipSnapshot actor, in AiShipSnapshot target)
    {
        var first = MemberEngageSeconds(actor, target);
        if (!float.IsFinite(first)) return 0;
        float best = float.PositiveInfinity, range = 0;
        var scale = SustainableScale(actor.Heat, WeaponHeatRate(actor.Primary) + WeaponHeatRate(actor.Secondary), MainFirstHeat(actor));
        ConsiderWorkingRange(actor, target, actor.Primary, scale, 1, first, ref best, ref range);
        ConsiderWorkingRange(actor, target, actor.Secondary, scale, 1, first, ref best, ref range);
        var pairs = ShipCatalog.TurretCount(actor.Id.Class) / 2;
        if (pairs > 0)
        {
            var heat = new Heat { Limit = 30, CoolingPerSecond = 12 };
            var cannon = ShipCatalog.TurretWeapon(actor.Id.Class, 0); var plasma = ShipCatalog.TurretWeapon(actor.Id.Class, 1);
            ConsiderWorkingRange(actor, target, cannon, SustainableScale(heat, WeaponHeatRate(cannon), FirstHeat(cannon)), pairs, first, ref best, ref range);
            ConsiderWorkingRange(actor, target, plasma, SustainableScale(heat, WeaponHeatRate(plasma), FirstHeat(plasma)), pairs, first, ref best, ref range);
        }
        return range;
    }

    private void ConsiderWorkingRange(in AiShipSnapshot actor, in AiShipSnapshot target, in Weapon weapon,
        float scale, int copies, float first, ref float best, ref float range)
    {
        if (!WeaponArrival(actor, target, weapon, out var distance, out var arrival, out var flight) ||
            arrival > first + Config.Tactics.MaximumJoinDelaySeconds) return;
        var profile = DamageRules.ForWeapon(weapon.Kind, Config.Damage);
        var dps = weapon.Damage * weapon.Burst * Config.TickRate * Config.ChurnMultiplier / weapon.Cooldown *
            scale * copies * HitProbability(target, distance, flight);
        var value = arrival + DamageRules.EstimateKillSeconds(target.Shield.Current, target.Hull.Current,
            target.Hull.Armor, dps * profile.ShieldMultiplier, dps * profile.HullMultiplier);
        if (value < best) { best = value; range = weapon.Range; }
    }

    private float MemberEngageSeconds(in AiShipSnapshot actor, in AiShipSnapshot target)
    {
        var first = float.PositiveInfinity;
        if (WeaponArrival(actor, target, actor.Primary, out _, out var arrival, out _)) first = arrival;
        if (WeaponArrival(actor, target, actor.Secondary, out _, out arrival, out _)) first = MathF.Min(first, arrival);
        if (ShipCatalog.TurretCount(actor.Id.Class) > 0)
        {
            if (WeaponArrival(actor, target, ShipCatalog.TurretWeapon(actor.Id.Class, 0), out _, out arrival, out _))
                first = MathF.Min(first, arrival);
            if (WeaponArrival(actor, target, ShipCatalog.TurretWeapon(actor.Id.Class, 1), out _, out arrival, out _))
                first = MathF.Min(first, arrival);
        }
        return first;
    }

    private float HeatReadySeconds(in AiShipSnapshot actor)
    {
        if (actor.Docked || !(actor.Heat.Limit > 0)) return float.PositiveInfinity;
        if (!actor.Overheated && actor.Heat.Value < actor.Heat.Limit) return 0;
        if (!(actor.Heat.CoolingPerSecond > 0)) return float.PositiveInfinity;
        var threshold = actor.Overheated ? actor.Heat.Limit * 0.45f : actor.Heat.Limit;
        return MathF.Max(0, (actor.Heat.Value - threshold) / actor.Heat.CoolingPerSecond) + 1f / Config.TickRate;
    }

    private bool WeaponArrival(in AiShipSnapshot actor, in AiShipSnapshot target, in Weapon weapon,
        out float firingDistance, out float arrival, out float flight, float readyFloor = 0)
    {
        firingDistance = 0; arrival = float.PositiveInfinity; flight = 0;
        if (!ScoringWeaponAvailable(weapon)) return false;
        var ready = HeatReadySeconds(actor);
        if (!float.IsFinite(ready)) return false;
        ready = MathF.Max(readyFloor, MathF.Max(ready,
            MathF.Max(0, (long)weapon.NextFireTick - Tick) / (float)Config.TickRate));
        if (ready > Config.Tactics.MaximumEngageSeconds) return false;
        var futureTarget = target.Transform.Position + target.Motion.Velocity * ready;
        var origin = actor.Transform.Position + actor.Motion.Velocity * ready;
        firingDistance = Vector2.Distance(origin, futureTarget);
        if (firingDistance > weapon.Range)
        {
            if (!(actor.Motion.MaxSpeed > 0.5f) ||
                ShipCatalog.IsCarrier(actor.Id.Class) && firingDistance > weapon.Range * 1.5f) return false;
            var pursuit = FireControl.Predict(origin, Vector2.Zero, futureTarget, target.Motion.Velocity,
                actor.Motion.MaxSpeed, weapon.Range * 0.9f);
            var extra = pursuit.InterceptTime;
            if (!(extra > 0) || !float.IsFinite(extra)) return false;
            ready += extra;
            if (ready > Config.Tactics.MaximumEngageSeconds) return false;
            origin += pursuit.Direction * actor.Motion.MaxSpeed * extra;
            futureTarget += target.Motion.Velocity * extra;
            firingDistance = Vector2.Distance(origin, futureTarget);
            if (firingDistance > weapon.Range * 1.0001f) return false;
        }
        var speed = weapon.Kind == WeaponKind.Missile ? 85 : weapon.Speed;
        var aim = FireControl.Predict(origin, actor.Motion.Velocity, futureTarget, target.Motion.Velocity, speed, actor.Radius + 0.9f);
        if (firingDistance > actor.Radius + target.Radius + 0.9f &&
            (aim.InterceptTime <= 0 || aim.InterceptTime > 4)) return false;
        flight = MathF.Max(1f / Config.TickRate, aim.InterceptTime);
        arrival = ready + flight;
        return float.IsFinite(arrival) && arrival <= Config.Tactics.MaximumEngageSeconds;
    }

    private float HitProbability(in AiShipSnapshot target, float distance, float flight, int targetIndex = -1)
    {
        float speed;
        if (targetIndex >= 0)
        {
            if (!targetSpeedReady[targetIndex])
            { targetSpeeds[targetIndex] = target.Motion.Velocity.Length(); targetSpeedReady[targetIndex] = true; }
            speed = targetSpeeds[targetIndex];
        }
        else speed = target.Motion.Velocity.Length();
        var radius = MathF.Max(0.01f, target.Radius);
        var maneuver = radius / (radius + speed * flight * 0.025f);
        var spread = distance * MathF.Tan(FireControl.MaxSpreadRadians);
        var spreadHit = spread > 0 ? Math.Clamp(radius / spread, 0, 1) : 1;
        return Math.Clamp(maneuver * (0.75f + 0.25f * spreadHit), 0.01f, 1);
    }

    private float WeaponHeatRate(Weapon weapon) => !ScoringWeaponAvailable(weapon) ? 0 :
        weapon.HeatPerShot * weapon.Burst * Config.TickRate * Config.ChurnMultiplier / weapon.Cooldown;

    private static float FirstHeat(in Weapon weapon) => ScoringWeaponAvailable(weapon)
        ? MathF.Max(0, weapon.HeatPerShot * weapon.Burst) : 0;
    private static float MainFirstHeat(in AiShipSnapshot actor) => FirstHeat(actor.Primary) + FirstHeat(actor.Secondary);
    private float SustainableScale(in Heat heat, float rate, float firstHeat) => rate <= 0 ? 1 :
        Math.Clamp((MathF.Max(0, heat.CoolingPerSecond) +
            MathF.Max(0, heat.Limit - heat.Value - firstHeat) / Config.Tactics.ScoringHorizonSeconds) / rate, 0, 1);

    private void PrepareActorScoring(int member, in AiShipSnapshot actor)
    {
        ref var scoring = ref actorScoring[member];
        if (scoring.Ready) return;
        scoring.HeatScale = SustainableScale(actor.Heat, WeaponHeatRate(actor.Primary) + WeaponHeatRate(actor.Secondary), MainFirstHeat(actor));
        // Catalog count/weapons, not live edited or reparented mounted entities; only solo estimates use them.
        scoring.MountCount = ShipCatalog.TurretCount(actor.Id.Class);
        if (scoring.MountCount > 0)
        { scoring.Cannon = ShipCatalog.TurretWeapon(actor.Id.Class, 0); scoring.Plasma = ShipCatalog.TurretWeapon(actor.Id.Class, 1); }
        scoring.Ready = true;
    }

    private void AddMemberStreams(int member, in AiShipSnapshot actor, in AiShipSnapshot target, int targetIndex,
        bool includeMounts, Span<TacticalFireStream> streams, ref int count)
    {
        var primaryFire = float.PositiveInfinity;
        float primaryHeat = 0;
        if (WeaponArrival(actor, target, actor.Primary, out var distance, out var arrival, out var flight))
        {
            primaryFire = arrival - flight;
            var burst = ReadyBurst(actor.Primary, actor.Heat.Limit - MathF.Max(0,
                actor.Heat.Value - actor.Heat.CoolingPerSecond * primaryFire));
            primaryHeat = actor.Primary.HeatPerShot * burst;
            PrepareActorScoring(member, actor);
            ref var scoring = ref actorScoring[member];
            AddStream(member, actor.Primary, target, targetIndex, distance, arrival, flight, scoring.HeatScale, 1, burst,
                ref scoring.PrimaryDps, streams, ref count);
        }
        if (WeaponArrival(actor, target, actor.Secondary, out distance, out arrival, out flight))
        {
            var ready = arrival - flight;
            var heat = SecondaryHeat(actor, primaryFire, primaryHeat, ready);
            var floor = heat < actor.Heat.Limit ? ready : actor.Heat.CoolingPerSecond > 0
                ? ready + (heat - actor.Heat.Limit) / actor.Heat.CoolingPerSecond + 1f / Config.TickRate
                : float.PositiveInfinity;
            if (WeaponArrival(actor, target, actor.Secondary, out distance, out arrival, out flight, floor))
            {
                var burst = ReadyBurst(actor.Secondary, actor.Heat.Limit -
                    SecondaryHeat(actor, primaryFire, primaryHeat, arrival - flight));
                PrepareActorScoring(member, actor);
                ref var scoring = ref actorScoring[member];
                AddStream(member, actor.Secondary, target, targetIndex, distance, arrival, flight, scoring.HeatScale, 1, burst,
                    ref scoring.SecondaryDps, streams, ref count);
            }
        }
        if (!includeMounts || ShipCatalog.TurretCount(actor.Id.Class) == 0) return;
        PrepareActorScoring(member, actor);
        ref var mounts = ref actorScoring[member];
        var pairs = mounts.MountCount / 2;
        var ownHeat = new Heat { Limit = 30, CoolingPerSecond = 12 };
        if (WeaponArrival(actor, target, mounts.Cannon, out distance, out arrival, out flight))
        {
            mounts.CannonHeatScale = SustainableScale(ownHeat, WeaponHeatRate(mounts.Cannon), FirstHeat(mounts.Cannon)); mounts.MountHeatReady = true;
            AddStream(member, mounts.Cannon, target, targetIndex, distance, arrival, flight, mounts.CannonHeatScale, pairs,
                ReadyBurst(mounts.Cannon, ownHeat.Limit),
                ref mounts.CannonDps, streams, ref count);
        }
        if (WeaponArrival(actor, target, mounts.Plasma, out distance, out arrival, out flight))
        {
            mounts.PlasmaHeatScale = SustainableScale(ownHeat, WeaponHeatRate(mounts.Plasma), FirstHeat(mounts.Plasma)); mounts.MountHeatReady = true;
            AddStream(member, mounts.Plasma, target, targetIndex, distance, arrival, flight, mounts.PlasmaHeatScale, pairs,
                ReadyBurst(mounts.Plasma, ownHeat.Limit),
                ref mounts.PlasmaDps, streams, ref count);
        }
    }

    private static int ReadyBurst(in Weapon weapon, float headroom) => headroom <= 0 ? 0 : weapon.HeatPerShot <= 0
        ? weapon.Burst : (int)Math.Clamp(MathF.Ceiling(headroom / weapon.HeatPerShot), 0, weapon.Burst);
    private static float SecondaryHeat(in AiShipSnapshot actor, float primaryFire, float primaryHeat, float fire) => primaryFire <= fire
        ? MathF.Max(0, MathF.Max(0, actor.Heat.Value - actor.Heat.CoolingPerSecond * primaryFire) + primaryHeat -
            actor.Heat.CoolingPerSecond * (fire - primaryFire))
        : MathF.Max(0, actor.Heat.Value - actor.Heat.CoolingPerSecond * fire);

    private void AddStream(int member, in Weapon weapon, in AiShipSnapshot target, int targetIndex, float distance,
        float arrival, float flight, float scale, int copies, int firstBurst, ref WeaponScoring scoring, Span<TacticalFireStream> streams, ref int count)
    {
        if (firstBurst <= 0) return;
        var profile = DamageRules.ForWeapon(weapon.Kind, Config.Damage);
        var probability = HitProbability(target, distance, flight, targetIndex);
        var resistance = MathF.Max(0, 1 - target.Hull.Armor);
        var capacity = MathF.Max(0, target.Shield.Current) / profile.ShieldMultiplier +
            (target.Hull.Current <= 0 ? 0 : resistance > 0
                ? target.Hull.Current / (profile.HullMultiplier * resistance) : float.PositiveInfinity);
        var shots = weapon.Burst * copies;
        var rawVolley = weapon.Damage * shots;
        var anyHit = 1 - MathF.Pow(1 - probability, shots);
        var sustainedVolley = MathF.Min(rawVolley * probability, capacity * anyHit);
        var firstShots = firstBurst * copies;
        var volley = MathF.Min(weapon.Damage * firstShots * probability,
            capacity * (1 - MathF.Pow(1 - probability, firstShots)));
        if (!(volley > 0) || !float.IsFinite(volley)) return;
        if (!scoring.Ready)
        {
            scoring.Dps = weapon.Damage * weapon.Burst * Config.TickRate * Config.ChurnMultiplier / weapon.Cooldown;
            scoring.Ready = true;
        }
        var cooldownTicks = Math.Max(1, (int)(weapon.Cooldown / Config.ChurnMultiplier));
        var cadenceCorrection = weapon.Cooldown / Config.ChurnMultiplier / cooldownTicks;
        var cap = sustainedVolley / (rawVolley * probability);
        var rate = scoring.Dps * copies * probability * cap * cadenceCorrection * scale;
        streams[count++] = new() { Member = member, Arrival = arrival, Volley = volley, Rate = rate, Profile = profile };
    }

    private float CounterLoss(in AiShipSnapshot enemy, in AiShipSnapshot actor, float exposure)
    {
        if (exposure <= 0) return 0;
        var scale = SustainableScale(enemy.Heat, WeaponHeatRate(enemy.Primary) + WeaponHeatRate(enemy.Secondary), MainFirstHeat(enemy));
        float shieldDamage = 0, hullDamage = 0;
        CounterWeapon(enemy, actor, enemy.Primary, scale, exposure, 1, ref shieldDamage, ref hullDamage);
        CounterWeapon(enemy, actor, enemy.Secondary, scale, exposure, 1, ref shieldDamage, ref hullDamage);
        var pairs = ShipCatalog.TurretCount(enemy.Id.Class) / 2;
        if (pairs > 0)
        {
            var heat = new Heat { Limit = 30, CoolingPerSecond = 12 };
            var cannon = ShipCatalog.TurretWeapon(enemy.Id.Class, 0); var plasma = ShipCatalog.TurretWeapon(enemy.Id.Class, 1);
            CounterWeapon(enemy, actor, cannon, SustainableScale(heat, WeaponHeatRate(cannon), FirstHeat(cannon)), exposure, pairs, ref shieldDamage, ref hullDamage);
            CounterWeapon(enemy, actor, plasma, SustainableScale(heat, WeaponHeatRate(plasma), FirstHeat(plasma)), exposure, pairs, ref shieldDamage, ref hullDamage);
        }
        var time = DamageRules.EstimateKillSeconds(actor.Shield.Current, actor.Hull.Current, actor.Hull.Armor, shieldDamage, hullDamage);
        return time > 0 ? Math.Clamp(1 / time, 0, 1) : 1;
    }

    private void CounterWeapon(in AiShipSnapshot enemy, in AiShipSnapshot actor, in Weapon weapon, float scale,
        float exposure, int copies, ref float shieldDamage, ref float hullDamage)
    {
        if (!WeaponArrival(enemy, actor, weapon, out var distance, out var arrival, out var flight) || arrival > exposure) return;
        var chance = HitProbability(actor, distance, flight);
        var profile = DamageRules.ForWeapon(weapon.Kind, Config.Damage);
        var ticks = Math.Max(1, (int)(weapon.Cooldown / Config.ChurnMultiplier));
        var volleys = 1 + MathF.Max(0, exposure - arrival) * Config.TickRate / ticks * scale;
        var damage = weapon.Damage * weapon.Burst * copies * chance * volleys;
        shieldDamage += damage * profile.ShieldMultiplier;
        hullDamage += damage * profile.HullMultiplier;
    }
}
