using System.Numerics;

namespace SpaceBattle;

public static class ArmamentRules
{
    public const string Revision = "mounted-armament-v7";
    public const float TurretTurnRate = 5, TurretAimTolerance = 0.08f, MainAimTolerance = 0.12f;
    public const int TurretDecisionTicks = 30, RecoilTicks = 9;

    public static int InitialHeavyChargeTicks(Weapon weapon, float churn, uint seed, int serial)
    {
        var cooldown = Math.Max(1, (int)(weapon.Cooldown / churn));
        var minimum = (cooldown + 1) / 2;
        // A separate stream staggers births without changing fleet placement or shot spread.
        var rng = new RandomStream(seed ^ unchecked((uint)serial * 0x9E3779B9u) ^ 0xD1B54A35u);
        return minimum + (int)(rng.Next() % (uint)(cooldown - minimum + 1));
    }
}

// Nongeneric value types avoid Mono WASM AOT generic calling-convention pitfalls.
internal readonly record struct ShotRequest(EntityKey Owner, EntityKey Target, int Serial, int Slot, Weapon Weapon,
    EntityKey Gun, Vector2 Origin = default, Vector2 Velocity = default, float Angle = 0);

public sealed partial class Battle<TStore>
{
    private readonly Dictionary<EntityKey, List<MovingRow>> attachedGuns = new();
    private readonly List<int> turretCandidates = new();
    private void SpawnTurrets(EntityKey parent, Identity id, Transform ship)
    {
        var count = ShipCatalog.TurretCount(id.Class);
        if (count == 0) return;
        var guns = new List<MovingRow>(count); attachedGuns.Add(parent, guns);
        var radius = ShipCatalog.Radius(id.Class);
        for (var slot = 0; slot < count; slot++)
        {
            var pairs = count / 2;
            var x = pairs == 1 ? 0 : 0.45f - 0.9f * (slot / 2) / (pairs - 1);
            var offset = new Vector2(x, slot % 2 == 0 ? -0.45f : 0.45f) * radius;
            var gun = Store.Create(); var number = ++serial;
            var position = ship.Position + Rotate(offset, ship.Angle);
            Store.Set(gun, new MountedTurret { Serial = number, Slot = slot, Parent = parent, Target = EntityKey.None,
                LocalOffset = offset, LastFireTick = -1000 });
            Store.Set(gun, new Transform { Position = position, Previous = position, Angle = ship.Angle });
            Store.Set(gun, ShipCatalog.TurretWeapon(id.Class, slot));
            Store.Set(gun, new Heat { Limit = 60, CoolingPerSecond = 12 });
            turrets.Add(new(gun, number)); Counters.StructuralAdds += 4;
            guns.Add(new(gun, number));
        }
    }

    private static Vector2 Rotate(Vector2 offset, float angle)
    {
        var c = MathF.Cos(angle); var s = MathF.Sin(angle);
        return new(offset.X * c - offset.Y * s, offset.X * s + offset.Y * c);
    }

    private void SyncTurrets()
    {
        foreach (var row in turrets)
        {
            ref var mount = ref Store.Get<MountedTurret>(row.Entity);
            if (!AliveShip(mount.Parent)) continue;
            var parent = Store.Get<Transform>(mount.Parent);
            ref var transform = ref Store.Get<Transform>(row.Entity);
            transform.Previous = transform.Position;
            transform.Position = parent.Position + Rotate(mount.LocalOffset, parent.Angle);
        }
    }

    private void UpdateTurrets()
    {
        SyncTurrets();
        foreach (var row in turrets)
        {
            var gun = row.Entity;
            ref var mount = ref Store.Get<MountedTurret>(gun);
            ref var heat = ref Store.Get<Heat>(gun);
            heat.Value = MathF.Max(0, heat.Value - heat.CoolingPerSecond / Config.TickRate);
            if (!AliveShip(mount.Parent) || Store.Has<Docked>(mount.Parent)) continue;
            var id = Store.Get<Identity>(mount.Parent);
            var brain = Store.Get<Brain>(mount.Parent);
            if (brain.Mode != BrainMode.Attack || Store.Has<Overheated>(mount.Parent)) continue;
            ref var weapon = ref Store.Get<Weapon>(gun);
            ref var transform = ref Store.Get<Transform>(gun);
            if (!TurretTargetAvailable(mount.Target, id.Team, transform.Position, weapon.Range))
            {
                mount.Target = EntityKey.None;
                if (Tick < mount.NextDecisionTick) continue;
                mount.NextDecisionTick = Tick + ArmamentRules.TurretDecisionTicks + mount.Slot;
                if (TurretTargetAvailable(brain.Target, id.Team, transform.Position, weapon.Range)) mount.Target = brain.Target;
                else
                {
                    var best = float.PositiveInfinity;
                    tacticalPoints.Query(transform.Position, weapon.Range, turretCandidates);
                    foreach (var candidate in turretCandidates)
                    {
                        var ship = aiRows[candidate];
                        if (!TurretTargetAvailable(ship.Entity, id.Team, transform.Position, weapon.Range)) continue;
                        var cost = Vector2.DistanceSquared(transform.Position, ship.Transform.Position);
                        if (cost < best) { best = cost; mount.Target = ship.Entity; }
                    }
                }
            }
            if (mount.Target.IsNone) continue;
            var parent = Store.Get<Transform>(mount.Parent);
            var motion = Store.Get<Motion>(mount.Parent);
            var offset = transform.Position - parent.Position;
            var velocity = motion.Velocity + new Vector2(-offset.Y, offset.X) * motion.AngularVelocity;
            var target = Store.Get<Transform>(mount.Target).Position;
            var targetVelocity = Store.Get<Motion>(mount.Target).Velocity;
            var radius = Store.Get<Collider>(mount.Parent).Radius + 0.8f;
            var direction = FireControl.Predict(transform.Position, velocity, target, targetVelocity, weapon.Speed, radius).Direction;
            var muzzle = MuzzleDistance(offset, direction, radius);
            direction = FireControl.Predict(transform.Position, velocity, target, targetVelocity, weapon.Speed, muzzle).Direction;
            var desired = MathF.Atan2(direction.Y, direction.X);
            transform.Angle = WrapAngle(transform.Angle + Math.Clamp(WrapAngle(desired - transform.Angle),
                -ArmamentRules.TurretTurnRate / Config.TickRate, ArmamentRules.TurretTurnRate / Config.TickRate));
            if (Tick < weapon.NextFireTick || heat.Value >= heat.Limit ||
                MathF.Abs(WrapAngle(desired - transform.Angle)) > ArmamentRules.TurretAimTolerance) continue;
            weapon.NextFireTick = Tick + Math.Max(1, (int)(weapon.Cooldown / Config.ChurnMultiplier));
            shots.Add(new(mount.Parent, mount.Target, mount.Serial, mount.Slot + 2, weapon,
                gun, transform.Position, velocity, transform.Angle));
        }
    }

    private bool TurretTargetAvailable(EntityKey target, int team, Vector2 position, float range) =>
        AliveShip(target) && !Store.Has<Docked>(target) && Store.Get<Identity>(target).Team != team &&
        Vector2.DistanceSquared(position, Store.Get<Transform>(target).Position) <= range * range;

    private static float MuzzleDistance(Vector2 offset, Vector2 direction, float radius)
    {
        // Keep the muzzle outside the parent's enclosing circle regardless of owner filtering.
        var projection = Vector2.Dot(offset, direction);
        return MathF.Max(0, -projection + MathF.Sqrt(MathF.Max(0, projection * projection + radius * radius - offset.LengthSquared())));
    }

    private void RefreshTurretAttachments()
    {
        foreach (var guns in attachedGuns.Values) guns.Clear();
        foreach (var row in turrets)
        {
            var parent = Store.Get<MountedTurret>(row.Entity).Parent;
            if (!attachedGuns.TryGetValue(parent, out var guns)) attachedGuns.Add(parent, guns = new());
            guns.Add(row);
        }
    }

    private void DestroyTurrets(EntityKey parent)
    {
        if (!attachedGuns.Remove(parent, out var guns)) return;
        // Retain reverse attachment destruction order; compact the global list once per tick.
        for (var i = guns.Count - 1; i >= 0; i--)
        {
            Store.Destroy(guns[i].Entity); Counters.StructuralRemoves += 4;
        }
    }
}
