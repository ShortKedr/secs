using System.Numerics;

namespace SpaceBattle;

public enum DebugColliderKind { Ship, Projectile, Debris }

public readonly record struct DebugCollider(int Serial, DebugColliderKind Kind, ProjectileShape Shape,
    Vector2 Position, float Angle, float Radius, Vector2 HalfExtents);

public sealed partial class Battle<TStore>
{
    /// <summary>Current physical shapes only, not sprite extents or swept volumes. No gameplay writes.</summary>
    /// <param name="refreshEntities">Keep true after direct Store edits; false reuses the lists finalized by Step.</param>
    public void CaptureColliders(List<DebugCollider> destination, bool refreshEntities = true)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (refreshEntities) Collect();
        destination.Clear();
        foreach (var row in ships)
        {
            var e = row.Entity;
            if (Store.Has<Docked>(e)) continue;
            var t = Store.Get<Transform>(e);
            var radius = Store.Get<Collider>(e).Radius;
            destination.Add(new(row.Identity.Serial, DebugColliderKind.Ship, ProjectileShape.Disk,
                t.Position, t.Angle, radius, new(radius)));
        }
        foreach (var row in projectiles)
        {
            var p = Store.Get<Projectile>(row.Entity);
            var t = Store.Get<Transform>(row.Entity);
            destination.Add(new(p.Serial, DebugColliderKind.Projectile, p.Shape, t.Position, t.Angle,
                p.Radius, p.Shape == ProjectileShape.Rectangle ? new(p.HalfLength, p.Radius) : new(p.Radius)));
        }
        foreach (var row in debris)
        {
            var p = Store.Get<Debris>(row.Entity);
            var t = Store.Get<Transform>(row.Entity);
            destination.Add(new(p.Serial, DebugColliderKind.Debris, ProjectileShape.Disk,
                t.Position, t.Angle, p.Radius, new(p.Radius)));
        }
    }
}
