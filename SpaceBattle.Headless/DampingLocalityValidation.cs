using System.Collections;
using System.Numerics;
using System.Reflection;
using SpaceBattle;

internal static class DampingLocalityValidation
{
    public static void Run()
    {
        CacheChecks();
        StoreChecks(static () => new SkecsStore());
        StoreChecks(static () => new LeoEcsLiteStore());
        StoreChecks(static () => new LeoEcsClassicStore());
        StoreChecks(static () => new MorpehStore());
        StoreChecks(static () => new StaticEcsStore());
        Console.WriteLine("PASS: bounded raw-bit damping cache, scalar steering parity, lazy row access and external Motion edits on five stores.");
    }

    private static readonly float[] Dampings = [0.15f, 0.4f, 0, -0f, -0.25f, float.Epsilon,
        float.MaxValue, float.MinValue, float.PositiveInfinity, float.NegativeInfinity,
        BitConverter.Int32BitsToSingle(0x7FC00001), BitConverter.Int32BitsToSingle(unchecked((int)0xFFC12345)),
        BitConverter.Int32BitsToSingle(0x7F800001)];

    private static void CacheChecks()
    {
        var cache = new DampingFactorCache();
        var entries = CacheEntries(cache);
        Check(entries.Length == DampingFactorCache.Capacity, "fixed cache capacity");
        foreach (var damping in Dampings)
        foreach (var dt in new[] { 1f / 60, 1f / 30, 0, -0f, -0.02f, float.Epsilon, float.MaxValue,
            float.PositiveInfinity, float.NegativeInfinity, BitConverter.Int32BitsToSingle(0x7FC23456) })
        {
            Same(cache.Get(damping, dt), MathF.Exp(-damping * dt), "cache miss matches scalar Exp");
            Same(cache.Get(damping, dt), MathF.Exp(-damping * dt), "cache repeat/bypass matches scalar Exp");
            if (float.IsFinite(damping) && float.IsFinite(dt))
                Check(FindEntry(entries, damping, dt) >= 0, "raw key includes damping and dt, including signed zero");
        }
        // A test-only sentinel proves a matching slot is returned rather than recomputed.
        cache.Get(0.15f, 1f / 60);
        var slot = FindEntry(entries, 0.15f, 1f / 60);
        var entry = entries.GetValue(slot)!;
        var factor = entry.GetType().GetField("Factor")!;
        var scalar = (float)factor.GetValue(entry)!;
        factor.SetValue(entry, 123.25f); entries.SetValue(entry, slot);
        Same(cache.Get(0.15f, 1f / 60), 123.25f, "matching slot is a real hit");
        Same(cache.Get(0.4f, 1f / 60), MathF.Exp(-0.4f * (1f / 60)), "coefficient change cannot hit old factor");
        Same(cache.Get(0.15f, 1f / 30), MathF.Exp(-0.15f * (1f / 30)), "dt change cannot hit old factor");
        factor.SetValue(entry, scalar); entries.SetValue(entry, slot);
        DistinctRawKeys(0, 1f / 60, BitConverter.Int32BitsToSingle(int.MinValue), 1f / 60);
        DistinctRawKeys(0.15f, 0, 0.15f, BitConverter.Int32BitsToSingle(int.MinValue));
        foreach (var count in new[] { 0, 1, 3, 17, 65, 257, 4099 })
        for (var pass = 0; pass < 2; pass++)
        for (var i = 0; i < count; i++)
        {
            var damping = (i - 13) * 0.013f;
            var dt = i % 2 == 0 ? 1f / 60 : 1f / 120;
            Same(cache.Get(damping, dt), MathF.Exp(-damping * dt), "empty/tail/distinct-key/collision scalar parity");
        }
        Check(ReferenceEquals(entries, CacheEntries(cache)) && entries.Length == DampingFactorCache.Capacity,
            "history cannot grow cache storage");
        var fresh = new DampingFactorCache();
        foreach (var damping in Dampings.Where(x => !float.IsFinite(x))) fresh.Get(damping, 1f / 60);
        fresh.Get(0.15f, float.NaN); fresh.Get(0.15f, float.PositiveInfinity);
        Check(FindOccupied(CacheEntries(fresh)) == 0, "nonfinite inputs bypass the cache");
        Check(FindOccupied(CacheEntries(new DampingFactorCache())) == 0, "cache instances do not share entries");
    }

    private static Array CacheEntries(DampingFactorCache cache) =>
        (Array)typeof(DampingFactorCache).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;

    private static void DistinctRawKeys(float damping, float dt, float otherDamping, float otherDt)
    {
        var cache = new DampingFactorCache(); cache.Get(damping, dt);
        var entries = CacheEntries(cache); var slot = FindEntry(entries, damping, dt);
        var entry = entries.GetValue(slot)!;
        entry.GetType().GetField("Factor")!.SetValue(entry, 123.25f); entries.SetValue(entry, slot);
        Same(cache.Get(otherDamping, otherDt), MathF.Exp(-otherDamping * otherDt), "signed zeros must use distinct raw keys");
    }

    private static int FindEntry(Array entries, float damping, float dt)
    {
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries.GetValue(i)!; var type = entry.GetType();
            if ((bool)type.GetField("Occupied")!.GetValue(entry)! &&
                (int)type.GetField("DampingBits")!.GetValue(entry)! == BitConverter.SingleToInt32Bits(damping) &&
                (int)type.GetField("DtBits")!.GetValue(entry)! == BitConverter.SingleToInt32Bits(dt)) return i;
        }
        return -1;
    }

    private static int FindOccupied(Array entries) => entries.Cast<object>().Count(entry =>
        (bool)entry.GetType().GetField("Occupied")!.GetValue(entry)!);

    private static void StoreChecks(Func<IEcsStore> factory)
    {
        using var battle = new Battle<TracingStore>(new TracingStore(factory()), new BattleConfig { ShipsPerTeam = 8, Seed = 27 });
        var store = battle.Store;
        var initial = new CollectTransforms { Rows = new() };
        store.ForEach<Transform, CollectTransforms>(ref initial);
        foreach (var e in initial.Rows) store.Destroy(e);
        store.Commit();
        // Isolate steering: no AI, physics, lifecycle, or cached-snapshot refresh can mask a stale damping value.
        foreach (var count in new[] { 0, 1, 3, 17, 65, 257 })
        {
            var rows = new List<EntityKey>();
            for (var i = 0; i < count; i++) rows.Add(Actor(store, i + 1, Dampings[i % Dampings.Length]));
            store.Commit();
            for (var pass = 0; pass < 2; pass++)
            {
                var expected = rows.ToDictionary(e => e, e => Scalar(battle, e));
                var steering = new Battle<TracingStore>.SteeringSystem { Battle = battle };
                store.ForEach<Transform, Motion, Brain, Battle<TracingStore>.SteeringSystem>(ref steering);
                foreach (var e in rows) Compare(store, e, expected[e]);
            }
            foreach (var e in rows) store.Destroy(e);
            store.Commit();
        }
        var actor = Actor(store, int.MaxValue, 0.15f);
        var target = Actor(store, 9, 0.4f);
        store.Get<Transform>(target).Position = new(70, 12);
        store.Get<Brain>(actor).Target = target;
        store.Get<Brain>(actor).Mode = BrainMode.Attack;
        store.Commit();
        VerifySelected(battle, actor);
        Check(store.Count("Alive", typeof(void), target) == 1 && store.Count("Has", typeof(Identity), target) == 1 &&
            store.Count("Get", typeof(Hull), target) == 1 && store.Count("Get", typeof(Transform), target) == 1,
            "attack target liveness and position are row-local single reads");
        Check(store.Count("Get", typeof(Weapon), actor) == 1 && store.Count("Get", typeof(Identity), actor) == 1 &&
            store.Count("Get", typeof(Collider), actor) == 0 && store.Count("Has", typeof(Overheated), actor) == 0,
            "actor reads reused; nonheavy collider and nonboost overheat calls stay lazy");
        store.Get<Weapon>(actor).Kind = WeaponKind.HeavyCannon;
        VerifySelected(battle, actor);
        Check(store.Count("Get", typeof(Collider), actor) == 1 && store.Count("Get", typeof(Transform), target) == 1,
            "heavy aiming reads collider only on demand and reuses target position");
        store.Set(actor, new Boosting { UntilTick = 100 }); store.Commit();
        store.Get<Transform>(actor).Position = new(battle.Config.ArenaHalfSize, -battle.Config.ArenaHalfSize);
        VerifySelected(battle, actor);
        store.Remove<Boosting>(actor); store.Commit();
        store.Get<Transform>(actor).Position = new(1, -2);
        foreach (var damping in new[] { 0.9f, 0.15f, -0f, float.PositiveInfinity, 0.4f })
        {
            { ref var liveMotion = ref store.Get<Motion>(actor); liveMotion.Damping = damping; liveMotion.Velocity = new(3, -2); }
            VerifySelected(battle, actor);
        }
        // The copy cache must be invalidated by every Execute, even for the same actor and target.
        store.Get<Identity>(actor).Class = ShipClass.LightCarrier;
        store.Get<Weapon>(actor).Range = 41;
        store.Get<Transform>(target).Position = new(-33, 25);
        VerifySelected(battle, actor);
        store.Get<Brain>(actor).Mode = BrainMode.Retreat; store.Get<Brain>(actor).Home = target;
        VerifySelected(battle, actor);
        Check(store.Count("Alive", typeof(void), target) == 1 && store.Count("Get", typeof(Transform), target) == 1,
            "retreat never evaluates the attack-target branch");
        store.Get<Brain>(actor).Mode = BrainMode.Screen; store.Get<Brain>(actor).CoverAlly = target;
        VerifySelected(battle, actor);
        store.Get<Brain>(actor).Mode = BrainMode.Attack;
        store.Get<Hull>(target).Current = 0;
        VerifySelected(battle, actor);
        Check(store.Count("Get", typeof(Transform), target) == 0 && store.Count("Get", typeof(Collider), actor) == 0,
            "dead target suppresses transform and heavy aiming reads");
        store.Destroy(target); store.Commit();
        VerifySelected(battle, actor);
        Check(store.Count("Alive", typeof(void), target) == 1 && store.Count("Has", typeof(Identity), target) == 0,
            "stale target retains AliveShip short circuit");
        Expect<InvalidOperationException>(() => { _ = store.Get<Motion>(target); });
        var replacement = Actor(store, 11, 0.4f); store.Commit();
        Check(!store.Alive(target), "replacement does not revive stale public identity");
        var nonship = store.Create(); store.Set(nonship, new Transform()); store.Commit();
        store.Get<Brain>(actor).Target = nonship;
        VerifySelected(battle, actor);
        Check(store.Count("Has", typeof(Identity), nonship) == 1 && store.Count("Get", typeof(Hull), nonship) == 0 &&
            store.Count("Get", typeof(Transform), nonship) == 0, "identity-less target retains Has short circuit");
        store.Get<Brain>(actor).Target = replacement;
        store.Get<Identity>(actor).Serial = 360;
        VerifySelected(battle, actor);
        Check(store.Count("Has", typeof(Overheated), actor) == 1 && store.Count("Get", typeof(Identity), actor) == 1,
            "queued boost reuses serial but retains conditional overheat read");
        var docked = store.Create();
        store.Set(docked, new Transform()); store.Set(docked, new Motion { Velocity = new(5, 7), Acceleration = new(1, 2) });
        store.Set(docked, new Brain { Mode = BrainMode.Attack, Target = target }); store.Set(docked, new Docked());
        store.Commit();
        VerifySelected(battle, docked);
        Check(store.Calls.Count == 1 && store.Count("Has", typeof(Docked), docked) == 1,
            "docked early exit never requires Identity/Weapon/Heat/target or damping");
        battle.Dispose();
        Check(!store.Alive(actor) && !store.Has<Motion>(actor), "disposed store retains public false liveness");
        Expect<ObjectDisposedException>(() => { _ = store.Get<Motion>(actor); });
        Expect<ObjectDisposedException>(() => battle.Step());
    }

    private static EntityKey Actor(TracingStore store, int serial, float damping)
    {
        var e = store.Create();
        store.Set(e, new Transform { Position = new(1, -2), Angle = 0.2f });
        store.Set(e, new Motion { Velocity = new(3, -2), AngularVelocity = 0.17f, MaxSpeed = 35, Thrust = 19, Damping = damping });
        store.Set(e, new Brain { Mode = BrainMode.Patrol, Home = EntityKey.None, Target = EntityKey.None,
            CoverAlly = EntityKey.None, SquadLeader = EntityKey.None, OrbitSign = 1 });
        store.Set(e, new Identity { Serial = serial, Class = ShipClass.Fighter });
        store.Set(e, new Weapon { Kind = WeaponKind.Cannon, Range = 90, Speed = 100 });
        store.Set(e, new Hull { Current = 100, Maximum = 100 }); store.Set(e, new Heat { Value = 7 });
        store.Set(e, new Collider { Radius = 2 });
        return e;
    }

    private readonly record struct Expected(Motion Motion, float? Heat, bool Boost);

    // Scalar oracle transcribed from data-locality-20261012/source/Battle.cs. Private tactical
    // helpers are shared unchanged; all steering arithmetic and repeated reads stay scalar here.
    private static Expected Scalar(Battle<TracingStore> battle, EntityKey e)
    {
        var store = battle.Store; var t = store.Get<Transform>(e); var m = store.Get<Motion>(e); var b = store.Get<Brain>(e);
        if (store.Has<Docked>(e)) { m.Velocity = m.Acceleration = Vector2.Zero; return new(m, null, false); }
        var dt = 1f / battle.Config.TickRate; var desired = Vector2.Zero;
        bool Alive(EntityKey key) => store.Alive(key) && store.Has<Identity>(key) && store.Get<Hull>(key).Current > 0;
        if (b.Mode == BrainMode.Screen && Alive(b.CoverAlly))
            desired = ClampMagnitude(Call<Vector2>(battle, "CoverVelocity", b.CoverAlly) +
                (Call<Vector2>(battle, "ScreenDestination", e, b) - t.Position) * 2, m.MaxSpeed);
        else if (b.Mode == BrainMode.Retreat && Alive(b.Home))
            desired = Direction(store.Get<Transform>(b.Home).Position - t.Position) * m.MaxSpeed;
        else if (Alive(b.Target))
        {
            var delta = store.Get<Transform>(b.Target).Position - t.Position;
            var carrier = ShipCatalog.IsCarrier(store.Get<Identity>(e).Class);
            var range = store.Get<Weapon>(e).Range * (carrier ? 0.65f : 0.48f);
            var direction = Direction(delta); var tangent = new Vector2(-direction.Y, direction.X) * b.OrbitSign;
            desired = direction * Math.Clamp((delta.Length() - range) * 0.7f, -m.MaxSpeed * 0.55f, m.MaxSpeed)
                + tangent * m.MaxSpeed * (carrier ? 0.15f : b.SquadLeader.IsNone ? 0.65f : 0.35f);
            desired += Call<Vector2>(battle, "FormationSteering", e, b, t.Position, m.MaxSpeed);
        }
        var facing = b.Mode == BrainMode.Attack && Alive(b.Target) ? store.Get<Transform>(b.Target).Position - t.Position : desired;
        var primary = store.Get<Weapon>(e);
        if (b.Mode == BrainMode.Attack && Alive(b.Target) && primary.Kind == WeaponKind.HeavyCannon)
            facing = FireControl.Predict(t.Position, m.Velocity, store.Get<Transform>(b.Target).Position,
                Call<Vector2>(battle, "CoverVelocity", b.Target), primary.Speed, store.Get<Collider>(e).Radius + 1.8f).Direction;
        var heavy = ShipCatalog.IsCarrier(store.Get<Identity>(e).Class);
        var desiredTurn = facing.LengthSquared() > 0.001f
            ? Math.Clamp(WrapAngle(MathF.Atan2(facing.Y, facing.X) - t.Angle) * 5, heavy ? -0.8f : -2.5f, heavy ? 0.8f : 2.5f) : 0;
        m.AngularVelocity += Math.Clamp(desiredTurn - m.AngularVelocity, -(heavy ? 1.5f : 10) * dt, (heavy ? 1.5f : 10) * dt);
        var inward = -t.Position;
        if (MathF.Abs(t.Position.X) > battle.Config.ArenaHalfSize - 15 || MathF.Abs(t.Position.Y) > battle.Config.ArenaHalfSize - 15)
            desired = Direction(inward) * m.MaxSpeed;
        m.Acceleration = ClampMagnitude((desired - m.Velocity) * 2, m.Thrust);
        var boost = store.Has<Boosting>(e) ? 1.5f : 1;
        m.Velocity = ClampMagnitude((m.Velocity + m.Acceleration * boost * dt) * MathF.Exp(-m.Damping * dt), m.MaxSpeed * boost);
        var queueBoost = battle.Tick % 360 == store.Get<Identity>(e).Serial % 360 && !store.Has<Overheated>(e);
        return new(m, store.Get<Heat>(e).Value + m.Acceleration.Length() * 0.055f * dt * boost, queueBoost);
    }

    private static T Call<T>(Battle<TracingStore> battle, string name, params object[] args) =>
        (T)typeof(Battle<TracingStore>).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(battle, args)!;

    private static IList States(Battle<TracingStore> battle) =>
        (IList)typeof(Battle<TracingStore>).GetField("states", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(battle)!;

    private static void VerifySelected(Battle<TracingStore> battle, EntityKey e)
    {
        var expected = Scalar(battle, e); var states = States(battle); var before = states.Count;
        battle.Store.Calls.Clear();
        var selected = new SelectedSteering { Entity = e, Steering = new() { Battle = battle } };
        battle.Store.ForEach<Transform, Motion, Brain, SelectedSteering>(ref selected);
        Check(selected.Count == 1 && states.Count == before + (expected.Boost ? 1 : 0), "selected row and boost queue parity");
        if (expected.Boost)
        {
            var request = states[states.Count - 1]!; var type = request.GetType();
            Check((EntityKey)type.GetProperty("Entity")!.GetValue(request)! == e &&
                (int)type.GetProperty("Serial")!.GetValue(request)! == battle.Store.Inner.Get<Identity>(e).Serial &&
                type.GetProperty("Kind")!.GetValue(request)!.ToString() == "Boost" &&
                (bool)type.GetProperty("Enable")!.GetValue(request)! &&
                (EntityKey)type.GetProperty("Other")!.GetValue(request)! == default, "boost request preserves all fields");
        }
        Compare(battle.Store.Inner, e, expected);
    }

    private static void Compare(IEcsStore store, EntityKey e, Expected expected)
    {
        var actual = store.Get<Motion>(e); var want = expected.Motion;
        Same(actual.Velocity.X, want.Velocity.X, "velocity X"); Same(actual.Velocity.Y, want.Velocity.Y, "velocity Y");
        Same(actual.Acceleration.X, want.Acceleration.X, "acceleration X"); Same(actual.Acceleration.Y, want.Acceleration.Y, "acceleration Y");
        Same(actual.AngularVelocity, want.AngularVelocity, "angular velocity");
        Same(actual.Damping, want.Damping, "damping unchanged"); Same(actual.MaxSpeed, want.MaxSpeed, "max speed unchanged");
        Same(actual.Thrust, want.Thrust, "thrust unchanged");
        if (expected.Heat is { } heat) Same(store.Get<Heat>(e).Value, heat, "heat expression order");
    }

    private struct CollectTransforms : IProcessor<Transform>
    { public List<EntityKey> Rows; public void Execute(EntityKey e, ref Transform t) => Rows.Add(e); }

    private struct SelectedSteering : IProcessor<Transform, Motion, Brain>
    {
        public EntityKey Entity; public Battle<TracingStore>.SteeringSystem Steering; public int Count;
        public void Execute(EntityKey e, ref Transform t, ref Motion m, ref Brain b)
        { if (e == Entity) { Count++; Steering.Execute(e, ref t, ref m, ref b); } }
    }

    private sealed class TracingStore(IEcsStore inner) : IEcsStore
    {
        public IEcsStore Inner { get; } = inner;
        public readonly Dictionary<(string Operation, Type Type, EntityKey Entity), int> Calls = new();
        private void Record(string operation, Type type, EntityKey e)
        { var key = (operation, type, e); Calls[key] = Calls.GetValueOrDefault(key) + 1; }
        public int Count(string operation, Type type, EntityKey e) => Calls.GetValueOrDefault((operation, type, e));
        public string Name => Inner.Name;
        public EntityKey Create() => Inner.Create();
        public void Destroy(EntityKey e) => Inner.Destroy(e);
        public bool Alive(EntityKey e) { Record("Alive", typeof(void), e); return Inner.Alive(e); }
        public bool Has<T>(EntityKey e) where T : unmanaged { Record("Has", typeof(T), e); return Inner.Has<T>(e); }
        public ref T Get<T>(EntityKey e) where T : unmanaged { Record("Get", typeof(T), e); return ref Inner.Get<T>(e); }
        public void Set<T>(EntityKey e, in T value) where T : unmanaged => Inner.Set(e, value);
        public void Remove<T>(EntityKey e) where T : unmanaged => Inner.Remove<T>(e);
        public void Commit() => Inner.Commit();
        public void Dispose() => Inner.Dispose();
        public StoreDiagnostics Diagnostics() => Inner.Diagnostics();
        public void ForEach<T, P>(ref P p) where T : unmanaged where P : struct, IProcessor<T> => Inner.ForEach<T, P>(ref p);
        public void ForEach<A, B, P>(ref P p) where A : unmanaged where B : unmanaged where P : struct, IProcessor<A, B> => Inner.ForEach<A, B, P>(ref p);
        public void ForEach<A, B, C, P>(ref P p) where A : unmanaged where B : unmanaged where C : unmanaged where P : struct, IProcessor<A, B, C> => Inner.ForEach<A, B, C, P>(ref p);
        public void ForEachComponents<A, B, C, P>(ref P p) where A : unmanaged where B : unmanaged where C : unmanaged where P : struct, IComponentProcessor<A, B, C> => Inner.ForEachComponents<A, B, C, P>(ref p);
    }

    private static Vector2 Direction(Vector2 v) => v.LengthSquared() > 0.0001f ? Vector2.Normalize(v) : Vector2.Zero;
    private static Vector2 ClampMagnitude(Vector2 v, float max) => v.LengthSquared() > max * max ? Direction(v) * max : v;
    private static float WrapAngle(float v) { while (v > MathF.PI) v -= MathF.Tau; while (v < -MathF.PI) v += MathF.Tau; return v; }
    private static void Same(float actual, float expected, string message) =>
        Check(BitConverter.SingleToInt32Bits(actual) == BitConverter.SingleToInt32Bits(expected), message + " bit-exact");
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Expect<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
