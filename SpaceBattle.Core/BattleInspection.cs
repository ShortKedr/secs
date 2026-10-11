using System.Numerics;

namespace SpaceBattle;

public sealed partial class Battle<TStore>
{
    public bool TryFindShip(int number, out EntityKey entity) => serialToShip.TryGetValue(number, out entity);
    public void Capture(BattleFrame frame)
    {
        Collect(); frame.Tick = Tick; frame.ArenaHalfSize = Config.ArenaHalfSize; frame.LastTickMs = LastTickMs; frame.Counters = Counters.Copy();
        frame.Ships.Clear(); frame.Projectiles.Clear(); frame.Debris.Clear(); frame.Particles.Clear();
        foreach (var row in ships)
        {
            var e = row.Entity; if (Store.Has<Docked>(e)) continue;
            var t = Store.Get<Transform>(e); var m = Store.Get<Motion>(e); var heat = Store.Get<Heat>(e); var collider = Store.Get<Collider>(e);
            frame.Ships.Add(new(row.Identity.Serial, row.Identity.Team, (int)row.Identity.Class, t.Position, m.Velocity, t.Angle, collider.Radius,
                row.Hull, row.Shield, heat.Value / heat.Limit, Vector4.One));
        }
        foreach (var row in projectiles)
        {
            var e = row.Entity; var p = Store.Get<Projectile>(e); var t = Store.Get<Transform>(e); var m = Store.Get<Motion>(e);
            frame.Projectiles.Add(new(p.Serial, -1, (int)p.Kind, t.Position, m.Velocity, t.Angle, p.Radius, 0, 0, 0,
                p.Kind == WeaponKind.Plasma ? new(0.2f, 1, 0.6f, 1) : p.Kind == WeaponKind.Missile ? new(1, 0.2f, 0.2f, 1) : new(1, 0.8f, 0.2f, 1)));
        }
        foreach (var row in debris)
        {
            var e = row.Entity; var p = Store.Get<Debris>(e); var t = Store.Get<Transform>(e); var m = Store.Get<Motion>(e);
            frame.Debris.Add(new(p.Serial, -1, 0, t.Position, m.Velocity, t.Angle, p.Radius, 0, 0, 0, new(0.7f, 0.72f, 0.8f, 1)));
        }
        foreach (var row in particles)
        {
            var e = row.Entity; var p = Store.Get<Particle>(e); var t = Store.Get<Transform>(e); var m = Store.Get<Motion>(e);
            var color = p.Color; color.W = Math.Clamp((p.ExpiresTick - Tick) / 30f, 0, 1);
            frame.Particles.Add(new(p.Serial, 0, p.Kind, t.Position, m.Velocity, t.Angle, p.Size, 0, 0, 0, color));
        }
    }
    public ulong Checksum()
    {
        Collect();
        ulong hash = 14695981039346656037;
        Add((uint)Tick); Add((uint)serial);
        foreach (var row in ships)
        {
            Add((uint)row.Identity.Serial); Add((uint)row.Identity.Class); Add((uint)row.Identity.Team);
            var e = row.Entity; var t = Store.Get<Transform>(e); var m = Store.Get<Motion>(e);
            TransformHash(t); MotionHash(m);
            var hull = Store.Get<Hull>(e); Float(hull.Current); Float(hull.Maximum); Float(hull.Armor);
            var shield = Store.Get<Shield>(e); Float(shield.Current); Float(shield.Maximum); Float(shield.RegenPerSecond); Add((uint)shield.LastHitTick); Add((uint)shield.RegenDelayTicks);
            var heat = Store.Get<Heat>(e); Float(heat.Value); Float(heat.Limit); Float(heat.CoolingPerSecond);
            var brain = Store.Get<Brain>(e); Ref(brain.Target); Ref(brain.Home); Add((uint)brain.Mode); Add((uint)brain.NextDecisionTick); Float(brain.OrbitSign);
            WeaponHash(Store.Get<Weapon>(e));
            Add(Store.Has<SecondaryWeapon>(e) ? 1u : 0); if (Store.Has<SecondaryWeapon>(e)) WeaponHash(Store.Get<SecondaryWeapon>(e).Value);
            var collider = Store.Get<Collider>(e); Float(collider.Radius); Float(collider.Mass); Add((uint)collider.ShapeCount);
            Add((uint)Store.Get<CombatState>(e).LastDamageTick);
            Add(Store.Has<Carrier>(e) ? 1u : 0);
            if (Store.Has<Carrier>(e)) { var c = Store.Get<Carrier>(e); Add((uint)c.NextBuildTick); Add((uint)c.BuildInterval); Add((uint)c.Capacity); Add((uint)c.Built); Add((uint)c.RepairSlots); }
            var inventory = Store.Get<Inventory>(e);
            Add((uint)inventory.Capacity); Add((uint)inventory.NextUseTick);
            for (var i = 0; i < 16; i++) { Add((uint)inventory.Items[i].Kind); Add((uint)inventory.Items[i].Count); }
            Add(Store.Has<Docked>(e) ? 1u : 0); if (Store.Has<Docked>(e)) { var d = Store.Get<Docked>(e); Ref(d.Carrier); Add((uint)d.ReleaseTick); }
            Add(Store.Has<Overheated>(e) ? 1u : 0); if (Store.Has<Overheated>(e)) Add((uint)Store.Get<Overheated>(e).SinceTick);
            Add(Store.Has<InCombat>(e) ? 1u : 0); if (Store.Has<InCombat>(e)) Add((uint)Store.Get<InCombat>(e).UntilTick);
            Add(Store.Has<Boosting>(e) ? 1u : 0); if (Store.Has<Boosting>(e)) Add((uint)Store.Get<Boosting>(e).UntilTick);
            Add(Store.Has<ShieldBroken>(e) ? 1u : 0); if (Store.Has<ShieldBroken>(e)) Add((uint)Store.Get<ShieldBroken>(e).SinceTick);
        }
        foreach (var row in projectiles)
        {
            var e = row.Entity; var p = Store.Get<Projectile>(e); Add((uint)p.Serial); Add((uint)p.Kind); Ref(p.Owner); Ref(p.Target);
            Float(p.Damage); Float(p.Radius); Add((uint)p.ExpiresTick); TransformHash(Store.Get<Transform>(e)); MotionHash(Store.Get<Motion>(e));
        }
        foreach (var row in debris)
        {
            var e = row.Entity; var p = Store.Get<Debris>(e); Add((uint)p.Serial); Float(p.Damage); Float(p.Radius); Add((uint)p.ExpiresTick);
            TransformHash(Store.Get<Transform>(e)); MotionHash(Store.Get<Motion>(e));
        }
        foreach (var row in particles)
        {
            var e = row.Entity; var p = Store.Get<Particle>(e); Add((uint)p.Serial); Add((uint)p.Kind); Float(p.Size); Add((uint)p.ExpiresTick);
            Float(p.Color.X); Float(p.Color.Y); Float(p.Color.Z); Float(p.Color.W); TransformHash(Store.Get<Transform>(e)); MotionHash(Store.Get<Motion>(e));
        }
        foreach (var r in reinforcements) { Add((uint)r.Tick); Add((uint)r.Spawn.Team); Add((uint)r.Spawn.Class); Float(r.Spawn.Position.X); Float(r.Spawn.Position.Y); }
        return hash;
        void Add(uint value) { hash ^= value; hash *= 1099511628211; }
        void Float(float value) => Add(BitConverter.SingleToUInt32Bits(value));
        void Ref(EntityKey e) => Add(AliveShip(e) ? (uint)Store.Get<Identity>(e).Serial : uint.MaxValue);
        void TransformHash(Transform t) { Float(t.Position.X); Float(t.Position.Y); Float(t.Previous.X); Float(t.Previous.Y); Float(t.Angle); }
        void MotionHash(Motion m) { Float(m.Velocity.X); Float(m.Velocity.Y); Float(m.Acceleration.X); Float(m.Acceleration.Y); Float(m.AngularVelocity); Float(m.MaxSpeed); Float(m.Thrust); Float(m.Damping); }
        void WeaponHash(Weapon w) { Add((uint)w.Kind); Add((uint)w.Cooldown); Add((uint)w.NextFireTick); Add((uint)w.Burst); Float(w.Range); Float(w.Damage); Float(w.Speed); Float(w.HeatPerShot); }
    }
    public void Validate()
    {
        Collect(); var seen = new HashSet<int>();
        foreach (var row in ships)
        {
            var e = row.Entity;
            if (!Store.Alive(e) || !seen.Add(row.Identity.Serial)) throw new InvalidOperationException("Invalid ship lifetime or duplicate serial.");
            var hull = Store.Get<Hull>(e); var shield = Store.Get<Shield>(e); var heat = Store.Get<Heat>(e); var inventory = Store.Get<Inventory>(e);
            var motion = Store.Get<Motion>(e); var t = Store.Get<Transform>(e); var collider = Store.Get<Collider>(e);
            if (!float.IsFinite(t.Position.X + t.Position.Y + t.Angle + motion.Velocity.X + motion.Velocity.Y + heat.Value +
                hull.Current + hull.Maximum + hull.Armor + shield.Current + shield.Maximum + shield.RegenPerSecond + heat.Limit + heat.CoolingPerSecond) ||
                hull.Current is < 0 || hull.Current > hull.Maximum || shield.Current < 0 || shield.Current > shield.Maximum || heat.Value < 0 ||
                inventory.Capacity is < 1 or > 16 || collider.ShapeCount != ShipCatalog.Shapes(row.Identity.Class))
                throw new InvalidOperationException($"Invalid ship state: {row.Identity.Serial}");
            for (var i = 0; i < inventory.Capacity; i++) if (inventory.Items[i].Count < 0) throw new InvalidOperationException("Negative inventory.");
        }
        if (physics.Bodies != ships.Count) throw new InvalidOperationException("Physics/ECS lifetime divergence.");
        foreach (var row in projectiles.Concat(debris).Concat(particles))
        {
            var t = Store.Get<Transform>(row.Entity); var m = Store.Get<Motion>(row.Entity);
            if (!Store.Alive(row.Entity) || !seen.Add(row.Serial) || !float.IsFinite(t.Position.X + t.Position.Y + m.Velocity.X + m.Velocity.Y))
                throw new InvalidOperationException("Invalid transient entity.");
        }
    }
}
