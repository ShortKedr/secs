using System.Numerics;

namespace SpaceBattle;

public sealed partial class Battle<TStore>
{
    private readonly List<(int Tick, SpawnRequest Spawn)> reinforcements = new();
    private struct WeaponSystem : IProcessor<Identity, Weapon, Brain>
    {
        public Battle<TStore> Battle;
        public void Execute(EntityKey e, ref Identity id, ref Weapon w, ref Brain b) => Battle.TryFire(e, id, ref w, b, 0);
    }
    private struct SecondarySystem : IProcessor<Identity, SecondaryWeapon, Brain>
    {
        public Battle<TStore> Battle;
        public void Execute(EntityKey e, ref Identity id, ref SecondaryWeapon w, ref Brain b) => Battle.TryFire(e, id, ref w.Value, b, 1);
    }
    private void TryFire(EntityKey e, Identity id, ref Weapon w, Brain b, int slot)
    {
        if (Tick < w.NextFireTick || b.Mode != BrainMode.Attack || !AliveShip(b.Target) || Store.Has<Docked>(e) || Store.Has<Overheated>(e) ||
            Store.Get<Heat>(e).Value >= Store.Get<Heat>(e).Limit) return;
        var distance = Vector2.DistanceSquared(Store.Get<Transform>(e).Position, Store.Get<Transform>(b.Target).Position);
        if (distance > w.Range * w.Range) return;
        if (w.Kind == WeaponKind.HeavyCannon)
        {
            var transform = Store.Get<Transform>(e);
            var direction = FireControl.Predict(transform.Position, Store.Get<Motion>(e).Velocity,
                Store.Get<Transform>(b.Target).Position, Store.Get<Motion>(b.Target).Velocity,
                w.Speed, Store.Get<Collider>(e).Radius + 1.8f).Direction;
            if (MathF.Abs(WrapAngle(MathF.Atan2(direction.Y, direction.X) - transform.Angle)) > ArmamentRules.MainAimTolerance) return;
        }
        w.NextFireTick = Tick + Math.Max(1, (int)(w.Cooldown / Config.ChurnMultiplier));
        shots.Add(new(e, b.Target, id.Serial, slot, w, EntityKey.None));
    }
    private void FlushShots()
    {
        shots.Sort(static (a, b) => a.Serial != b.Serial ? a.Serial.CompareTo(b.Serial) : a.Slot.CompareTo(b.Slot));
        foreach (var shot in shots)
        {
            var t = Store.Get<Transform>(shot.Owner); var velocity = Store.Get<Motion>(shot.Owner).Velocity;
            var weapon = shot.Weapon;
            var mounted = !shot.Gun.IsNone;
            ref var heat = ref Store.Get<Heat>(mounted ? shot.Gun : shot.Owner);
            var target = Store.Get<Transform>(shot.Target).Position;
            var muzzleOffset = Store.Get<Collider>(shot.Owner).Radius + (weapon.Kind == WeaponKind.HeavyCannon ? 1.8f : 0.8f);
            var aim = FireControl.Predict(t.Position, velocity, target, Store.Get<Motion>(shot.Target).Velocity,
                weapon.Speed, muzzleOffset).Direction;
            var spread = FireControl.Spread(Config.Seed, shot.Serial, Tick, shot.Slot, out var imprecise);
            Counters.AimDecisions++;
            if (imprecise) Counters.ImpreciseAimDecisions++;
            var angle = (mounted ? shot.Angle : MathF.Atan2(aim.Y, aim.X)) + spread;
            var forward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var origin = mounted ? shot.Origin : t.Position;
            if (mounted)
            {
                velocity = shot.Velocity;
                muzzleOffset = MuzzleDistance(origin - t.Position, forward, Store.Get<Collider>(shot.Owner).Radius + 0.8f);
            }
            for (var burst = 0; burst < weapon.Burst; burst++)
            {
                if (heat.Value >= heat.Limit) { if (!mounted) State(shot.Owner, StateKind.Overheat, true); break; }
                if (projectiles.Count >= Config.MaxProjectiles) break;
                var position = origin + forward * muzzleOffset;
                var e = Store.Create(); var number = ++serial;
                Store.Set(e, new Transform { Position = position, Previous = position, Angle = angle });
                Store.Set(e, new Motion { Velocity = velocity + forward * weapon.Speed, MaxSpeed = weapon.Speed * 1.5f });
                Store.Set(e, new Projectile { Serial = number, Owner = shot.Owner, Target = shot.Target,
                    Kind = weapon.Kind, Damage = weapon.Damage, Radius = weapon.Kind == WeaponKind.MachineGun ? 0.12f : weapon.Kind == WeaponKind.HeavyCannon ? 0.9f : 0.45f, ExpiresTick = Tick + 240 });
                projectiles.Add(new(e, number)); Counters.Shots++; Counters.StructuralAdds += 3;
                if (mounted) { Counters.TurretShots++; Store.Get<MountedTurret>(shot.Gun).LastFireTick = Tick; }
                if (weapon.Kind == WeaponKind.HeavyCannon) Counters.HeavyShots++;
                heat.Value += weapon.HeatPerShot;
            }
        }
        shots.Clear(); Store.Commit();
    }
    private struct CarrierSystem : IProcessor<Identity, Carrier, Transform>
    {
        public Battle<TStore> Battle;
        public void Execute(EntityKey e, ref Identity id, ref Carrier c, ref Transform t)
        {
            if (Battle.Tick < c.NextBuildTick) return;
            c.NextBuildTick = Battle.Tick + c.BuildInterval;
            if (Battle.teamCounts[id.Team] >= Battle.Config.ShipsPerTeam + Battle.Config.CarriersPerTeam) return;
            var rng = new RandomStream(Battle.Config.Seed ^ (uint)(id.Serial * 1009 + Battle.Tick));
            var offset = new Vector2(rng.Range(-16, 16), rng.Range(-16, 16));
            Battle.builds.Add(new(id.Team, (ShipClass)(c.Built % 8), t.Position + offset, e, id.Serial));
        }
    }
    private struct DockSystem : IProcessor<Identity, Docked, Hull>
    {
        public Battle<TStore> Battle;
        public void Execute(EntityKey e, ref Identity id, ref Docked dock, ref Hull h)
        {
            if (!Battle.AliveShip(dock.Carrier)) { Battle.State(e, StateKind.Undock, false); return; }
            var home = Battle.Store.Get<Transform>(dock.Carrier).Position;
            ref var t = ref Battle.Store.Get<Transform>(e); t.Previous = t.Position; t.Position = home;
            var before = h.Current;
            h.Current = MathF.Min(h.Maximum, h.Current + h.Maximum * 0.12f / Battle.Config.TickRate);
            ref var shield = ref Battle.Store.Get<Shield>(e); shield.Current = MathF.Min(shield.Maximum, shield.Current + shield.Maximum * 0.18f / Battle.Config.TickRate);
            ref var heat = ref Battle.Store.Get<Heat>(e); heat.Value = MathF.Max(0, heat.Value - heat.CoolingPerSecond * 3 / Battle.Config.TickRate);
            if (h.Current > before) Battle.Counters.Repairs++;
            if (Battle.Tick >= dock.ReleaseTick && h.Current >= h.Maximum * 0.98f && heat.Value < heat.Limit * 0.3f)
                Battle.State(e, StateKind.Undock, false);
        }
    }
    private void StepPhysics()
    {
        var dt = 1f / Config.TickRate;
        foreach (var row in ships)
        {
            ref var t = ref Store.Get<Transform>(row.Entity); ref var motion = ref Store.Get<Motion>(row.Entity);
            t.Previous = t.Position;
            physics.SetShip(Store.Get<PhysicsLink>(row.Entity).Body, t, motion, !Store.Has<Docked>(row.Entity));
        }
        physics.Step(dt);
        foreach (var row in ships)
        {
            if (Store.Has<Docked>(row.Entity)) continue;
            var state = physics.GetShip(Store.Get<PhysicsLink>(row.Entity).Body);
            ref var t = ref Store.Get<Transform>(row.Entity); ref var motion = ref Store.Get<Motion>(row.Entity);
            t.Position = state.Position; t.Angle = state.Angle; motion.Velocity = state.Velocity; motion.AngularVelocity = state.AngularVelocity;
        }
        var projectile = new ProjectileMotionSystem { Battle = this }; Store.ForEach<Transform, Motion, Projectile, ProjectileMotionSystem>(ref projectile);
        var fragment = new DebrisMotionSystem { Dt = dt }; Store.ForEach<Transform, Motion, Debris, DebrisMotionSystem>(ref fragment);
        var particle = new ParticleMotionSystem { Dt = dt }; Store.ForEach<Transform, Motion, Particle, ParticleMotionSystem>(ref particle);
    }
    private struct ProjectileMotionSystem : IProcessor<Transform, Motion, Projectile>
    {
        public Battle<TStore> Battle;
        public void Execute(EntityKey e, ref Transform t, ref Motion m, ref Projectile p)
        {
            var dt = 1f / Battle.Config.TickRate; t.Previous = t.Position;
            if (p.Kind == WeaponKind.Missile && Battle.AliveShip(p.Target))
            {
                var direction = Direction(Battle.Store.Get<Transform>(p.Target).Position - t.Position);
                m.Acceleration = direction * 50; m.Velocity = ClampMagnitude(m.Velocity + m.Acceleration * dt, 85);
            }
            t.Position += m.Velocity * dt; t.Angle = MathF.Atan2(m.Velocity.Y, m.Velocity.X);
        }
    }
    private struct DebrisMotionSystem : IProcessor<Transform, Motion, Debris>
    {
        public float Dt;
        public void Execute(EntityKey e, ref Transform t, ref Motion m, ref Debris d)
        { t.Previous = t.Position; t.Position += m.Velocity * Dt; t.Angle += m.AngularVelocity * Dt; }
    }
    private struct ParticleMotionSystem : IProcessor<Transform, Motion, Particle>
    {
        public float Dt;
        public void Execute(EntityKey e, ref Transform t, ref Motion m, ref Particle p)
        { t.Previous = t.Position; t.Position += m.Velocity * Dt; }
    }
    private void ResolveHits()
    {
        foreach (var contact in physics.Contacts)
        {
            if (contact.RelativeSpeed < 10) continue;
            if (serialToShip.TryGetValue(contact.FirstSerial, out var a) && AliveShip(a)) Damage(a, (contact.RelativeSpeed - 10) * 0.35f, false, false);
            if (serialToShip.TryGetValue(contact.SecondSerial, out var b) && AliveShip(b)) Damage(b, (contact.RelativeSpeed - 10) * 0.35f, false, false);
        }
        foreach (var row in projectiles)
        {
            ref var p = ref Store.Get<Projectile>(row.Entity); var t = Store.Get<Transform>(row.Entity);
            if (p.ExpiresTick <= Tick || Outside(t.Position)) { retired.Add(row.Entity); continue; }
            if (!physics.Sweep(t.Previous, t.Position, p.Radius, out var targetSerial, out var impact)) continue;
            if (!serialToShip.TryGetValue(targetSerial, out var target) || !AliveShip(target)) continue;
            Damage(target, p.Damage, p.Kind == WeaponKind.Plasma, false, p.Owner); Counters.Hits++;
            if (Store.Alive(p.Owner) && Store.Has<Identity>(p.Owner) &&
                Store.Get<Identity>(p.Owner).Team == Store.Get<Identity>(target).Team) Counters.FriendlyFireHits++;
            if (target == p.Owner) Counters.SelfHits++;
            var velocity = Store.Get<Motion>(row.Entity).Velocity;
            Effect(impact, velocity * 0.05f, p.Kind == WeaponKind.HeavyCannon ? 3 : p.Kind == WeaponKind.Plasma ? 1 : 0,
                p.Kind == WeaponKind.HeavyCannon ? 12 : 5);
            if (p.Kind is WeaponKind.Cannon or WeaponKind.Missile) Fragments(impact, velocity * 0.12f, 6, p.Damage * 0.2f);
            retired.Add(row.Entity);
        }
        foreach (var row in debris)
        {
            ref var p = ref Store.Get<Debris>(row.Entity); var t = Store.Get<Transform>(row.Entity);
            if (p.ExpiresTick <= Tick || Outside(t.Position)) { retired.Add(row.Entity); continue; }
            // Fragments damage either faction. No engine or artificial velocity decay.
            if (!physics.Sweep(t.Previous, t.Position, p.Radius, out var targetSerial, out var impact)) continue;
            if (!serialToShip.TryGetValue(targetSerial, out var target) || !AliveShip(target)) continue;
            var speed = Store.Get<Motion>(row.Entity).Velocity.Length();
            Damage(target, p.Damage * MathF.Max(0.2f, speed / 40), false, true);
            Counters.DebrisHits++; Effect(impact, Vector2.Zero, 0, 2); retired.Add(row.Entity);
        }
        foreach (var row in particles) if (Store.Get<Particle>(row.Entity).ExpiresTick <= Tick) retired.Add(row.Entity);
    }
    private bool Outside(Vector2 position) => MathF.Abs(position.X) > Config.ArenaHalfSize * 2.5f || MathF.Abs(position.Y) > Config.ArenaHalfSize * 2.5f;
    public void Damage(EntityKey e, float amount, bool plasma, bool fragment)
        => Damage(e, amount, plasma, fragment, EntityKey.None);
    public void Damage(EntityKey e, float amount, bool plasma, bool fragment, EntityKey attacker)
    {
        if (!AliveShip(e)) return;
        ref var shield = ref Store.Get<Shield>(e); ref var hull = ref Store.Get<Hull>(e);
        var absorb = MathF.Min(shield.Current, amount); shield.Current -= absorb; shield.LastHitTick = Tick;
        if (plasma && absorb > 0) { Store.Get<Heat>(e).Value += absorb * 1.8f; Counters.PlasmaShieldHits++; }
        hull.Current = MathF.Max(0, hull.Current - (amount - absorb) * (1 - hull.Armor));
        ref var combat = ref Store.Get<CombatState>(e);
        combat.LastDamageTick = Tick;
        combat.LastAttacker = AliveShip(attacker) && Store.Get<Identity>(attacker).Team != Store.Get<Identity>(e).Team
            ? attacker : EntityKey.None;
        State(e, StateKind.Combat, true);
        if (shield.Current <= 0) State(e, StateKind.ShieldBreak, true);
        if (hull.Current <= 0) State(e, StateKind.Destroy, true);
    }
    private void Effect(Vector2 position, Vector2 velocity, int kind, int count)
    {
        var rng = new RandomStream(Config.Seed ^ (uint)(serial * 113 + Tick));
        for (var i = 0; i < count * Config.ParticleMultiplier; i++)
        {
            if (particles.Count >= Config.MaxParticles) break;
            var e = Store.Create(); var number = ++serial;
            Store.Set(e, new Transform { Position = position, Previous = position });
            Store.Set(e, new Motion { Velocity = velocity + new Vector2(rng.Range(-8, 8), rng.Range(-8, 8)) });
            Store.Set(e, new Particle { Serial = number, ExpiresTick = Tick + 20 + i % 35, Kind = kind, Size = kind == 2 ? rng.Range(1, 4) : rng.Range(0.15f, 0.8f),
                Color = kind == 3 ? new(0.45f, 0.85f, 1, 1) : kind == 1 ? new(0.2f, 1, 0.8f, 1) : kind == 2 ? new(1, 0.35f, 0.1f, 1) : new(1, 0.8f, 0.2f, 1) });
            particles.Add(new(e, number)); Counters.StructuralAdds += 3;
        }
    }
    private void Fragments(Vector2 position, Vector2 velocity, int count, float damage)
    {
        var rng = new RandomStream(Config.Seed ^ (uint)(serial * 73 + Tick));
        for (var i = 0; i < count; i++)
        {
            if (debris.Count >= Config.MaxDebris) break;
            var angle = rng.Range(0, MathF.Tau); var speed = rng.Range(15, 65);
            var e = Store.Create(); var number = ++serial;
            Store.Set(e, new Transform { Position = position, Previous = position, Angle = angle });
            Store.Set(e, new Motion { Velocity = velocity + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed, AngularVelocity = rng.Range(-8, 8) });
            Store.Set(e, new Debris { Serial = number, Radius = rng.Range(0.08f, 0.3f), Damage = damage, ExpiresTick = Tick + 3600 });
            debris.Add(new(e, number)); Counters.StructuralAdds += 3;
        }
    }
    private void FlushLifecycle()
    {
        states.Sort(static (a, b) => a.Serial != b.Serial ? a.Serial.CompareTo(b.Serial) : a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : a.Enable.CompareTo(b.Enable));
        foreach (var state in states)
        {
            var e = state.Entity; if (!Store.Alive(e)) continue;
            switch (state.Kind)
            {
                case StateKind.Overheat:
                    var temperature = Store.Get<Heat>(e);
                    if (state.Enable && temperature.Value < temperature.Limit || !state.Enable && temperature.Value >= temperature.Limit * 0.45f) break;
                    if (Tag(e, state.Enable, new Overheated { SinceTick = Tick }) && state.Enable) Counters.Overheats++; break;
                case StateKind.Combat: Tag(e, state.Enable, new InCombat { UntilTick = Tick + 240 }); break;
                case StateKind.Boost: Tag(e, state.Enable, new Boosting { UntilTick = Tick + (int)(60 * Config.ChurnMultiplier) }); break;
                case StateKind.ShieldBreak:
                    if (Tag(e, state.Enable, new ShieldBroken { SinceTick = Tick }) && state.Enable) Counters.ShieldBreaks++; break;
                case StateKind.Dock:
                    if (Store.Has<Docked>(e) || !AliveShip(state.Other)) break;
                    var occupied = 0;
                    foreach (var ship in ships) if (Store.Alive(ship.Entity) && Store.Has<Docked>(ship.Entity) && Store.Get<Docked>(ship.Entity).Carrier == state.Other) occupied++;
                    if (occupied >= Store.Get<Carrier>(state.Other).RepairSlots) break;
                    Tag(e, true, new Docked { Carrier = state.Other, ReleaseTick = Tick + 180 });
                    Store.Get<Brain>(e).Mode = BrainMode.Docking; Counters.Dockings++; break;
                case StateKind.Undock:
                    if (!Store.Has<Docked>(e)) break;
                    var home = Store.Get<Docked>(e).Carrier;
                    Tag(e, false, default(Docked)); Store.Get<Brain>(e).Mode = BrainMode.Attack;
                    ref var inventory = ref Store.Get<Inventory>(e);
                    for (var i = 0; i < inventory.Capacity; i++) inventory.Items[i].Count = 2;
                    if (AliveShip(home)) Store.Get<Transform>(e).Position += new Vector2(Store.Get<Collider>(home).Radius + 5, state.Serial % 2 == 0 ? 6 : -6);
                    break;
                case StateKind.Destroy:
                    var id = Store.Get<Identity>(e); var position = Store.Get<Transform>(e).Position; var velocity = Store.Get<Motion>(e).Velocity;
                    var radius = Store.Get<Collider>(e).Radius;
                    physics.RemoveShip(Store.Get<PhysicsLink>(e).Body); serialToShip.Remove(id.Serial);
                    Counters.StructuralRemoves += ShipComponentCount(e);
                    DestroyTurrets(e);
                    Store.Destroy(e); Counters.ShipsDestroyed++; Counters.Explosions++;
                    Effect(position, velocity, 2, (int)(radius * 12)); Fragments(position, velocity, (int)(radius * 10), radius * 2);
                    if (ShipCatalog.IsCarrier(id.Class) && Config.ReplenishCarriers)
                        reinforcements.Add((Tick + 1800, new(id.Team, id.Class, new Vector2(id.Team == 0 ? -110 : 110, ((int)id.Class - 9) * 34), EntityKey.None)));
                    break;
            }
        }
        states.Clear();
        // Native destruction is deferred until all hit queries have completed.
        foreach (var e in retired) if (Store.Alive(e)) { Store.Destroy(e); Counters.StructuralRemoves += 3; }
        retired.Clear(); Store.Commit();
        builds.Sort(static (a, b) => a.Team != b.Team ? a.Team.CompareTo(b.Team) : a.Order.CompareTo(b.Order));
        foreach (var build in builds)
        {
            if (!AliveShip(build.Home) || teamCounts[build.Team] >= Config.ShipsPerTeam + Config.CarriersPerTeam) continue;
            var owned = 0;
            foreach (var ship in ships) if (AliveShip(ship.Entity) && Store.Get<Brain>(ship.Entity).Home == build.Home) owned++;
            if (owned >= Store.Get<Carrier>(build.Home).Capacity) continue;
            SpawnShip(build.Team, build.Class, build.Position, build.Home); Store.Get<Carrier>(build.Home).Built++;
            teamCounts[build.Team]++; Counters.Builds++;
        }
        builds.Clear();
        for (var i = reinforcements.Count - 1; i >= 0; i--)
        {
            if (reinforcements[i].Tick > Tick) continue;
            var spawn = reinforcements[i].Spawn; SpawnShip(spawn.Team, spawn.Class, spawn.Position, spawn.Home); reinforcements.RemoveAt(i);
        }
    }
    private bool Tag<T>(EntityKey e, bool enabled, T value) where T : unmanaged
    {
        var existing = Store.Has<T>(e);
        if (enabled) { Store.Set(e, value); if (!existing) Counters.StructuralAdds++; }
        else if (existing) { Store.Remove<T>(e); Counters.StructuralRemoves++; }
        return enabled != existing;
    }
    private int ShipComponentCount(EntityKey e) => 12 + (Store.Has<SecondaryWeapon>(e) ? 1 : 0) + (Store.Has<Carrier>(e) ? 1 : 0) +
        (Store.Has<Docked>(e) ? 1 : 0) + (Store.Has<InCombat>(e) ? 1 : 0) + (Store.Has<Overheated>(e) ? 1 : 0) +
        (Store.Has<Boosting>(e) ? 1 : 0) + (Store.Has<ShieldBroken>(e) ? 1 : 0);
    public void Dispose()
    {
        if (disposed) return; disposed = true; physics.Dispose(); Store.Dispose();
    }
}
