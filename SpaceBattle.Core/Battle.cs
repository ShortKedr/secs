using System.Diagnostics;
using System.Numerics;

namespace SpaceBattle;

public sealed partial class Battle<TStore> : IDisposable where TStore : IEcsStore
{
    public static readonly string[] StageNames = ["collect", "ai", "thermal-inventory", "steering", "weapons", "carriers", "physics", "hits", "lifecycle"];
    public TStore Store { get; }
    public BattleConfig Config { get; }
    public int Tick { get; private set; }
    public string Framework => Store.Name;
    public BattleCounters Counters { get; } = new();
    public AiCounters Tactics { get; } = new();
    public double LastTickMs { get; private set; }
    public double[] LastStageMs { get; } = new double[StageNames.Length];
    public int ShipCount => ships.Count;
    public int ProjectileCount => projectiles.Count;
    public int DebrisCount => debris.Count;
    public int ParticleCount => particles.Count;
    public int TurretCount => turrets.Count;
    public int TeamShipCount(int team) => team is >= 0 and <= 1 ? teamCounts[team] : throw new ArgumentOutOfRangeException(nameof(team));
    public int PhysicsBodies => physics.Bodies;
    public int PhysicsFixtures => physics.Fixtures;
    public PhysicsQueryStatistics PhysicsQueries => physics.QueryStatistics;
    public PhysicsTimingStatistics PhysicsTimings => physics.Timings;
    private readonly PhysicsService physics;
    private readonly DampingFactorCache dampingFactors = new();
    private readonly List<ShipRow> ships = new();
    private readonly List<MovingRow> projectiles = new(), debris = new(), particles = new();
    private readonly List<MovingRow> turrets = new();
    private readonly List<ShotRequest> shots = new();
    private readonly List<StateRequest> states = new();
    private readonly List<SpawnRequest> builds = new();
    private readonly List<EntityKey> retired = new();
    private readonly Dictionary<int, EntityKey> serialToShip = new();
    private readonly int[] teamCounts = new int[2];
    private int serial;
    private bool disposed;
    private readonly record struct ShipRow(EntityKey Entity, Identity Identity, Transform Transform, float Hull, float Shield);
    private readonly record struct MovingRow(EntityKey Entity, int Serial);
    private readonly record struct SpawnRequest(int Team, ShipClass Class, Vector2 Position, EntityKey Home, int Order = 0);
    private enum StateKind { Overheat, Combat, Boost, ShieldBreak, Dock, Undock, Destroy }
    private readonly record struct StateRequest(EntityKey Entity, int Serial, StateKind Kind, bool Enable, EntityKey Other);

    public Battle(TStore store, BattleConfig config)
    {
        config.Validate();
        var deployment = config.Deployment == FleetDeployment.Tactical ? FleetDeploymentRules.Create(config) : null;
        Config = config; Store = store; physics = new(config.Physics);
        var rng = new RandomStream(config.Seed);
        for (var team = 0; team < 2; team++)
        {
            var homes = new EntityKey[config.CarriersPerTeam];
            for (var i = 0; i < homes.Length; i++)
                homes[i] = SpawnShip(team, ShipClass.LightCarrier + i,
                    deployment is null ? new Vector2(team == 0 ? -90 : 90, (i - (homes.Length - 1) * 0.5f) * 34)
                        : Reflect(deployment.Carriers[i], team), EntityKey.None);
            for (var i = 0; i < config.ShipsPerTeam; i++)
                SpawnShip(team, FleetCompositionRules.Escort(config.FleetComposition, i),
                    deployment is null ? new Vector2((team == 0 ? -1 : 1) * rng.Range(40, 72), rng.Range(-65, 65))
                        : Reflect(deployment.Escorts[i], team), homes[i % homes.Length]);
        }
        Store.Commit(); Collect();
        InitializeWarpReinforcements();
        static Vector2 Reflect(Vector2 position, int team) => new(team == 0 ? -position.X : position.X, position.Y);
    }

    public EntityKey SpawnShip(int team, ShipClass kind, Vector2 position, EntityKey home)
    {
        var e = Store.Create(); var radius = ShipCatalog.Radius(kind); var carrier = ShipCatalog.IsCarrier(kind);
        var hull = carrier ? 5000 + ((int)kind - 8) * 3000 : 90 + (int)kind * 42;
        var shield = carrier ? hull * 0.45f : hull * 0.7f;
        var id = new Identity { Serial = ++serial, Team = team, Class = kind };
        var transform = new Transform { Position = position, Previous = position, Angle = team == 0 ? 0 : MathF.PI };
        var collider = new Collider { Radius = radius, ShapeCount = ShipCatalog.Shapes(kind), Mass = radius * radius * 8 };
        Store.Set(e, id); Store.Set(e, transform);
        Store.Set(e, new Motion { Velocity = new Vector2(team == 0 ? 4 : -4, 0), MaxSpeed = carrier ? 5 : 40 - (int)kind * 2,
            Thrust = carrier ? 1.2f : 25 - (int)kind * 1.5f, Damping = carrier ? 0.4f : 0.15f });
        Store.Set(e, new Hull { Current = hull, Maximum = hull, Armor = carrier ? 0.4f : (int)kind * 0.025f });
        Store.Set(e, new Shield { Current = shield, Maximum = shield, RegenPerSecond = shield * 0.075f, RegenDelayTicks = 240, LastHitTick = -1000 });
        Store.Set(e, new Heat { Limit = carrier ? 220 : 100 + (int)kind * 10, CoolingPerSecond = carrier ? 20 : 8 + (int)kind });
        Store.Set(e, new Brain { Home = home, Target = EntityKey.None, SquadLeader = EntityKey.None,
            CoverAlly = EntityKey.None, OrbitSign = id.Serial % 2 == 0 ? 1 : -1 });
        var primary = ShipCatalog.Primary(kind);
        if (primary.Kind == WeaponKind.HeavyCannon)
            primary.NextFireTick = Tick + ArmamentRules.InitialHeavyChargeTicks(primary, Config.ChurnMultiplier, Config.Seed, id.Serial);
        Store.Set(e, primary); Store.Set(e, collider);
        Store.Set(e, new PhysicsLink { Body = physics.AddShip(id.Serial, transform, collider) });
        Store.Set(e, new CombatState { LastDamageTick = -1000, LastAttacker = EntityKey.None });
        var inventory = new Inventory { Capacity = carrier ? 16 : 2 + (int)kind };
        for (var i = 0; i < inventory.Capacity; i++)
            inventory.Items[i] = new InventoryItem { Kind = i % 3 == 0 ? ItemKind.ShieldCell : i % 3 == 1 ? ItemKind.HeatSink : ItemKind.RepairKit, Count = carrier ? 10 : 2 };
        Store.Set(e, inventory);
        if (kind >= ShipClass.Corvette && !ShipCatalog.IsWeaponsPlatform(kind))
        {
            var secondary = ShipCatalog.Primary((ShipClass)(((int)kind + 1) % 4)); secondary.Range *= 1.1f;
            Store.Set(e, new SecondaryWeapon { Value = secondary });
        }
        if (carrier) Store.Set(e, new Carrier { BuildInterval = (int)(240 / Config.ChurnMultiplier), NextBuildTick = Tick + 120 + id.Serial % 90,
            Capacity = Config.ShipsPerTeam / Config.CarriersPerTeam + 8, RepairSlots = 4 });
        serialToShip.Add(id.Serial, e); Counters.ShipsCreated++;
        Counters.StructuralAdds += 12 + (kind >= ShipClass.Corvette && !ShipCatalog.IsWeaponsPlatform(kind) ? 1 : 0) + (carrier ? 1 : 0);
        SpawnTurrets(e, id, transform);
        return e;
    }

    public void Step()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var start = Stopwatch.GetTimestamp(); var previous = start;
        Tick++;
        Collect(); Mark(0);
        PrepareTactics();
        var ai = new AiSystem { Battle = this }; Store.ForEach<Identity, Brain, Transform, AiSystem>(ref ai); Mark(1);
        var thermal = new ThermalSystem { Battle = this }; Store.ForEach<Identity, Heat, Shield, ThermalSystem>(ref thermal);
        var inventory = new InventorySystem { Battle = this }; Store.ForEach<Identity, Inventory, Hull, InventorySystem>(ref inventory); Mark(2);
        var steering = new SteeringSystem { Battle = this }; Store.ForEach<Transform, Motion, Brain, SteeringSystem>(ref steering); Mark(3);
        var weapon = new WeaponSystem { Battle = this }; Store.ForEach<Identity, Weapon, Brain, WeaponSystem>(ref weapon);
        var second = new SecondarySystem { Battle = this }; Store.ForEach<Identity, SecondaryWeapon, Brain, SecondarySystem>(ref second);
        UpdateTurrets();
        FlushShots(); Mark(4);
        var carrier = new CarrierSystem { Battle = this }; Store.ForEach<Identity, Carrier, Transform, CarrierSystem>(ref carrier);
        var docking = new DockSystem { Battle = this }; Store.ForEach<Identity, Docked, Hull, DockSystem>(ref docking); Mark(5);
        StepPhysics(); Mark(6);
        ResolveHits(); Mark(7);
        FlushLifecycle(); Store.Commit(); CompactMovingRows(); SyncTurrets(); CollectShips();
        UpdateWarpReinforcements(); Mark(8);
        LastTickMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        void Mark(int stage) { var now = Stopwatch.GetTimestamp(); LastStageMs[stage] = (now - previous) * 1000.0 / Stopwatch.Frequency; previous = now; }
    }

    private void Collect()
    {
        CollectShips();
        projectiles.Clear(); debris.Clear(); particles.Clear();
        var projectilesCollector = new ProjectileCollector { Battle = this }; Store.ForEach<Projectile, ProjectileCollector>(ref projectilesCollector);
        var debrisCollector = new DebrisCollector { Battle = this }; Store.ForEach<Debris, DebrisCollector>(ref debrisCollector);
        var particlesCollector = new ParticleCollector { Battle = this }; Store.ForEach<Particle, ParticleCollector>(ref particlesCollector);
        projectiles.Sort(static (a, b) => a.Serial.CompareTo(b.Serial));
        debris.Sort(static (a, b) => a.Serial.CompareTo(b.Serial));
        particles.Sort(static (a, b) => a.Serial.CompareTo(b.Serial));
    }

    private void CollectShips()
    {
        ships.Clear(); Array.Clear(teamCounts);
        var collector = new ShipCollector { Battle = this }; Store.ForEach<Identity, Transform, ShipCollector>(ref collector);
        ships.Sort(static (a, b) => a.Identity.Serial.CompareTo(b.Identity.Serial));
    }

    private void CompactMovingRows()
    {
        Compact(projectiles); Compact(debris); Compact(particles); Compact(turrets);
        void Compact(List<MovingRow> rows)
        {
            var write = 0; var ordered = true; var previous = int.MinValue;
            for (var read = 0; read < rows.Count; read++)
            {
                var row = rows[read];
                if (!Store.Alive(row.Entity)) continue;
                ordered &= row.Serial >= previous; previous = row.Serial;
                rows[write++] = row;
            }
            if (write < rows.Count) rows.RemoveRange(write, rows.Count - write);
            // Normal spawns append increasing serials; externally injected serials may not.
            if (!ordered) rows.Sort(static (a, b) => a.Serial.CompareTo(b.Serial));
        }
    }

    private struct ShipCollector : IProcessor<Identity, Transform>
    {
        public Battle<TStore> Battle;
        public void Execute(EntityKey e, ref Identity id, ref Transform t)
        {
            ref var hull = ref Battle.Store.Get<Hull>(e); ref var shield = ref Battle.Store.Get<Shield>(e);
            Battle.ships.Add(new(e, id, t, hull.Current / hull.Maximum, shield.Current / shield.Maximum));
            Battle.teamCounts[id.Team]++;
        }
    }
    private struct ProjectileCollector : IProcessor<Projectile>
    { public Battle<TStore> Battle; public void Execute(EntityKey e, ref Projectile p) => Battle.projectiles.Add(new(e, p.Serial)); }
    private struct DebrisCollector : IProcessor<Debris>
    { public Battle<TStore> Battle; public void Execute(EntityKey e, ref Debris p) => Battle.debris.Add(new(e, p.Serial)); }
    private struct ParticleCollector : IProcessor<Particle>
    { public Battle<TStore> Battle; public void Execute(EntityKey e, ref Particle p) => Battle.particles.Add(new(e, p.Serial)); }

    private void State(EntityKey e, StateKind kind, bool enabled, EntityKey other = default)
        => states.Add(new(e, Store.Get<Identity>(e).Serial, kind, enabled, other));
    private bool AliveShip(EntityKey e) => Store.Alive(e) && Store.Has<Identity>(e) && Store.Get<Hull>(e).Current > 0;
    private EntityKey NearestHome(int team, Vector2 position)
    {
        var best = float.MaxValue; var home = EntityKey.None;
        foreach (var row in ships)
        {
            if (row.Identity.Team != team || !ShipCatalog.IsCarrier(row.Identity.Class) || !AliveShip(row.Entity)) continue;
            var d = Vector2.DistanceSquared(position, row.Transform.Position);
            if (d < best) { best = d; home = row.Entity; }
        }
        return home;
    }
    private struct AiSystem : IProcessor<Identity, Brain, Transform>
    {
        public Battle<TStore> Battle;
        public void Execute(EntityKey e, ref Identity id, ref Brain b, ref Transform t)
            => Battle.DecideTactics(e, id, ref b, t);
    }

    private struct ThermalSystem : IProcessor<Identity, Heat, Shield>
    {
        public Battle<TStore> Battle;
        public void Execute(EntityKey e, ref Identity id, ref Heat h, ref Shield s)
        {
            var dt = 1f / Battle.Config.TickRate;
            h.Value = MathF.Max(0, h.Value - h.CoolingPerSecond * dt);
            var overheated = Battle.Store.Has<Overheated>(e);
            if (Battle.Tick - s.LastHitTick >= s.RegenDelayTicks && !overheated)
                s.Current = MathF.Min(s.Maximum, s.Current + s.RegenPerSecond * dt);
            if (!overheated && h.Value >= h.Limit) Battle.State(e, StateKind.Overheat, true);
            else if (overheated && h.Value < h.Limit * 0.45f) Battle.State(e, StateKind.Overheat, false);
            if (Battle.Store.Has<ShieldBroken>(e) && s.Current > s.Maximum * 0.2f) Battle.State(e, StateKind.ShieldBreak, false);
            if (Battle.Store.Has<InCombat>(e) && Battle.Store.Get<InCombat>(e).UntilTick < Battle.Tick) Battle.State(e, StateKind.Combat, false);
            if (Battle.Store.Has<Boosting>(e) && Battle.Store.Get<Boosting>(e).UntilTick < Battle.Tick) Battle.State(e, StateKind.Boost, false);
        }
    }

    private struct InventorySystem : IProcessor<Identity, Inventory, Hull>
    {
        public Battle<TStore> Battle;
        public void Execute(EntityKey e, ref Identity id, ref Inventory inventory, ref Hull hull)
        {
            if (inventory.NextUseTick > Battle.Tick) return;
            ref var shield = ref Battle.Store.Get<Shield>(e); ref var heat = ref Battle.Store.Get<Heat>(e);
            for (var slot = 0; slot < inventory.Capacity; slot++)
            {
                ref var item = ref inventory.Items[slot]; if (item.Count <= 0) continue;
                var use = false;
                switch (item.Kind)
                {
                    case ItemKind.ShieldCell when shield.Current < shield.Maximum * 0.25f:
                        shield.Current = MathF.Min(shield.Maximum, shield.Current + shield.Maximum * 0.4f); use = true; break;
                    case ItemKind.HeatSink when heat.Value > heat.Limit * 0.85f:
                        heat.Value = MathF.Max(0, heat.Value - heat.Limit * 0.55f); use = true; break;
                    case ItemKind.RepairKit when hull.Current < hull.Maximum * 0.5f && Battle.Tick - shield.LastHitTick > 180:
                        hull.Current = MathF.Min(hull.Maximum, hull.Current + hull.Maximum * 0.2f); use = true; break;
                }
                if (!use) continue;
                item.Count--; inventory.NextUseTick = Battle.Tick + 180; Battle.Counters.ItemUses++; break;
            }
        }
    }

    internal struct SteeringSystem : IProcessor<Transform, Motion, Brain>
    {
        public Battle<TStore> Battle;
        public void Execute(EntityKey e, ref Transform t, ref Motion m, ref Brain b)
        {
            if (Battle.Store.Has<Docked>(e)) { m.Velocity = m.Acceleration = Vector2.Zero; return; }
            var dt = 1f / Battle.Config.TickRate;
            var desired = Vector2.Zero;
            // These values are lazy and row-local. Steering only writes Motion/Heat and queues states;
            // no structural mutation or Identity/Weapon/target Transform/Hull write occurs in this query.
            var targetAlive = false;
            var targetPosition = Vector2.Zero;
            var identityKnown = false; var identity = default(Identity);
            var primaryKnown = false; var primary = default(Weapon);
            if (b.Mode == BrainMode.Screen && Battle.AliveShip(b.CoverAlly))
                desired = ClampMagnitude(Battle.CoverVelocity(b.CoverAlly) +
                    (Battle.ScreenDestination(e, b) - t.Position) * 2, m.MaxSpeed);
            else if (b.Mode == BrainMode.Retreat && Battle.AliveShip(b.Home))
                desired = Direction(Battle.Store.Get<Transform>(b.Home).Position - t.Position) * m.MaxSpeed;
            else
            {
                targetAlive = Battle.AliveShip(b.Target);
                if (targetAlive)
                {
                    targetPosition = Battle.Store.Get<Transform>(b.Target).Position;
                    var delta = targetPosition - t.Position;
                    identity = Battle.Store.Get<Identity>(e); identityKnown = true;
                    var carrier = ShipCatalog.IsCarrier(identity.Class);
                    primary = Battle.Store.Get<Weapon>(e); primaryKnown = true;
                    var grouped = !b.SquadLeader.IsNone && Battle.squadIndexes.TryGetValue(b.SquadLeader, out _);
                    var range = grouped ? Battle.GroupWorkingRange(e, b.Target, primary.Range) * 0.72f : primary.Range * (carrier ? 0.65f : 0.48f);
                    var direction = Direction(delta);
                    var tangent = new Vector2(-direction.Y, direction.X) * b.OrbitSign;
                    desired = direction * Math.Clamp((delta.Length() - range) * 0.7f, -m.MaxSpeed * 0.55f, m.MaxSpeed)
                        + tangent * m.MaxSpeed * (carrier ? 0.15f : grouped ? 0.12f : b.SquadLeader.IsNone ? 0.65f : 0.35f);
                    desired += Battle.FormationSteering(e, b, t.Position, m.MaxSpeed);
                }
            }
            // Attack cannot take the Screen/Retreat branches, so its target check already ran.
            var attackingTarget = b.Mode == BrainMode.Attack && targetAlive;
            var facing = attackingTarget ? targetPosition - t.Position : desired;
            if (!primaryKnown) primary = Battle.Store.Get<Weapon>(e);
            if (attackingTarget && primary.Kind == WeaponKind.HeavyCannon)
                facing = FireControl.Predict(t.Position, m.Velocity, targetPosition,
                    Battle.CoverVelocity(b.Target), primary.Speed, Battle.Store.Get<Collider>(e).Radius + 1.8f).Direction;
            if (!identityKnown) identity = Battle.Store.Get<Identity>(e);
            var heavy = ShipCatalog.IsCarrier(identity.Class);
            var desiredTurn = facing.LengthSquared() > 0.001f
                ? Math.Clamp(WrapAngle(MathF.Atan2(facing.Y, facing.X) - t.Angle) * 5, heavy ? -0.8f : -2.5f, heavy ? 0.8f : 2.5f) : 0;
            m.AngularVelocity += Math.Clamp(desiredTurn - m.AngularVelocity, -(heavy ? 1.5f : 10) * dt, (heavy ? 1.5f : 10) * dt);
            var inward = -t.Position;
            if (MathF.Abs(t.Position.X) > Battle.Config.ArenaHalfSize - 15 || MathF.Abs(t.Position.Y) > Battle.Config.ArenaHalfSize - 15)
                desired = Direction(inward) * m.MaxSpeed;
            m.Acceleration = ClampMagnitude((desired - m.Velocity) * 2, m.Thrust);
            var boost = Battle.Store.Has<Boosting>(e) ? 1.5f : 1;
            m.Velocity = ClampMagnitude((m.Velocity + m.Acceleration * boost * dt) * Battle.dampingFactors.Get(m.Damping, dt), m.MaxSpeed * boost);
            if (Battle.Tick % 360 == identity.Serial % 360 && !Battle.Store.Has<Overheated>(e))
                Battle.states.Add(new(e, identity.Serial, StateKind.Boost, true, default));
            ref var heat = ref Battle.Store.Get<Heat>(e); heat.Value += m.Acceleration.Length() * 0.055f * dt * boost;
        }
    }
    private static Vector2 Direction(Vector2 v) => v.LengthSquared() > 0.0001f ? Vector2.Normalize(v) : Vector2.Zero;
    private static Vector2 ClampMagnitude(Vector2 v, float max) => v.LengthSquared() > max * max ? Direction(v) * max : v;
    private static float WrapAngle(float v) { while (v > MathF.PI) v -= MathF.Tau; while (v < -MathF.PI) v += MathF.Tau; return v; }
}
