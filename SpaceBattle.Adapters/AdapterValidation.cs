namespace SpaceBattle;

// Callable from the central validation host; this library does not add an executable.
public static class AdapterValidation
{
    public static string[] RunAll(int recycleCount = 70_000)
    {
        Func<IEcsStore>[] factories =
        [
            static () => new LeoEcsLiteStore(),
            static () => new LeoEcsClassicStore(),
            static () => new MorpehStore(),
            static () => new StaticEcsStore()
        ];
        var passed = new List<string>();
        foreach (var factory in factories)
        {
            using var store = factory();
            Console.WriteLine($"Validating {store.Name}");
            Validate(store, recycleCount);
            passed.Add(store.Name);
        }
        // Guard rejection must not reset the active instance; disposal must release it.
        using (var first = new StaticEcsStore())
        {
            ExpectFailure(static () => { using var unexpected = new StaticEcsStore(); });
            var e = first.Create();
            Require(first.Alive(e), "Static world was changed by rejected constructor.");
        }
        using (var next = new StaticEcsStore(r => { r.Register<Probe>(); r.Register<Probe>(); }))
        {
            var e = next.Create();
            next.Set(e, new Probe { Value = 19 });
            Require(next.Get<Probe>(e).Value == 19, "Additional static component registration failed.");
        }
        ExpectFailure(static () => { using var failed = new StaticEcsStore(_ => throw new InvalidOperationException("Registration failure probe.")); });
        using (var recovered = new StaticEcsStore()) Require(recovered.Alive(recovered.Create()), "Static constructor recovery failed.");
        return passed.ToArray();
    }

    public static void Validate(IEcsStore store, int recycleCount = 70_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recycleCount);
        var empty = store.Create();
        Require(store.Alive(empty), "Empty entity must be alive.");
        Require(!store.Alive(EntityKey.None) && !store.Alive(new EntityKey(int.MaxValue, 1)), "Invalid key is alive.");
        ExpectFailure(() => { var value = store.Get<Identity>(empty); });
        Require(!store.Has<Identity>(empty), "Get added an absent component.");
        CheckComponent<Identity>(store, empty);
        CheckComponent<Transform>(store, empty);
        CheckComponent<Motion>(store, empty);
        CheckComponent<Hull>(store, empty);
        CheckComponent<Shield>(store, empty);
        CheckComponent<Heat>(store, empty);
        CheckComponent<Brain>(store, empty);
        CheckComponent<Weapon>(store, empty);
        CheckComponent<SecondaryWeapon>(store, empty);
        CheckComponent<Collider>(store, empty);
        CheckComponent<PhysicsLink>(store, empty);
        CheckComponent<CombatState>(store, empty);
        CheckComponent<Carrier>(store, empty);
        CheckComponent<Docked>(store, empty);
        CheckComponent<Overheated>(store, empty);
        CheckComponent<InCombat>(store, empty);
        CheckComponent<Boosting>(store, empty);
        CheckComponent<ShieldBroken>(store, empty);
        CheckComponent<Projectile>(store, empty);
        CheckComponent<Debris>(store, empty);
        CheckComponent<Particle>(store, empty);
        CheckComponent<Lifetime>(store, empty);
        CheckComponent<InventoryItem>(store, empty);
        CheckComponent<InventoryItems>(store, empty);
        CheckComponent<Inventory>(store, empty);
        store.Commit();
        Require(store.Alive(empty), "Removing the last component destroyed the entity.");
        store.Destroy(empty);
        Require(!store.Alive(empty), "Destroyed entity is alive before commit.");
        store.Commit();

        var a = Seed(store, 1, true, true);
        var b = Seed(store, 2, true, false);
        var c = Seed(store, 3, false, false);
        store.Commit();
        var one = new One { Expected = new Dictionary<EntityKey, int> { [a] = 1, [b] = 2, [c] = 3 }, Seen = new() };
        store.ForEach<Identity, One>(ref one);
        Require(one.Count == 3 && one.Seen.Count == 3, "One-component query membership mismatch.");
        var two = new Two { Expected = new Dictionary<EntityKey, int> { [a] = 11, [b] = 12 }, Seen = new() };
        store.ForEach<Identity, Motion, Two>(ref two);
        Require(two.Count == 2 && two.Seen.Count == 2, "Two-component query membership mismatch.");
        var three = new Three { Expected = a };
        store.ForEach<Identity, Motion, Hull, Three>(ref three);
        Require(three.Count == 1, "Three-component query membership mismatch.");
        Require(store.Get<Identity>(a).Serial == 111 && store.Get<Identity>(b).Serial == 112
            && store.Get<Identity>(c).Serial == 13, "Query identity ref writes were lost.");
        Require(store.Get<Motion>(a).MaxSpeed == 6 && store.Get<Motion>(b).MaxSpeed == 5
            && store.Get<Hull>(a).Current == 7, "Query component ref writes were lost.");
        store.Get<Hull>(a).Current = 20;
        Require(store.Get<Hull>(a).Current == 20, "Get is not a live native ref.");
        store.Set(a, new Hull { Current = 30 });
        Require(store.Get<Hull>(a).Current == 30, "Set did not replace an existing component.");

        var mutation = new Mutation { Store = store };
        store.ForEach<Identity, Mutation>(ref mutation);
        Require(mutation.Count == 3, "Mutation guard query mismatch.");
        var throws = new Throws();
        ExpectFailure(() => store.ForEach<Identity, Throws>(ref throws));
        store.Remove<Motion>(b);
        store.Commit();
        two = new Two { Expected = new Dictionary<EntityKey, int> { [a] = 111 }, Seen = new() };
        store.ForEach<Identity, Motion, Two>(ref two);
        Require(two.Count == 1, "Cached query did not observe component removal.");
        store.Destroy(a); store.Destroy(b); store.Destroy(c); store.Commit();
        var none = new Count();
        store.ForEach<Identity, Count>(ref none);
        Require(none.Value == 0 && store.Diagnostics().Entities == 0, "Query retained destroyed entities.");

        var previous = store.Create();
        int reusableId = previous.Id;
        for (int i = 0; i < recycleCount; i++)
        {
            var stale = previous;
            store.Destroy(stale); store.Commit();
            previous = store.Create();
            Require(previous.Id == reusableId && previous.Generation != stale.Generation, "Logical slot was not safely recycled.");
            Require(!store.Alive(stale) && !store.Has<Identity>(stale), "Stale key became alive.");
            store.Set(previous, new Identity { Serial = i });
            store.Destroy(stale);
            store.Remove<Identity>(stale);
            Require(store.Alive(previous) && store.Get<Identity>(previous).Serial == i, "Stale operation touched the replacement.");
            ExpectFailure(() => store.Set(stale, new Identity()));
            store.Commit();
        }
        store.Destroy(previous); store.Commit();
        Require(!store.Alive(empty), "Old key revived after native generation rollover.");
        store.Dispose();
        Require(!store.Alive(previous), "Disposed store reports alive.");
        ExpectFailure(() => store.Create());
        store.Dispose();
    }

    private struct Probe { public int Value; }
    private static EntityKey Seed(IEcsStore s, int serial, bool motion, bool hull)
    {
        var e = s.Create();
        s.Set(e, new Identity { Serial = serial });
        if (motion) s.Set(e, new Motion { MaxSpeed = 3 });
        if (hull) s.Set(e, new Hull { Current = 4 });
        return e;
    }
    private static void CheckComponent<T>(IEcsStore s, EntityKey e) where T : unmanaged
    {
        s.Set(e, default(T));
        Require(s.Has<T>(e), $"Missing {typeof(T).Name} after Set.");
        ref var value = ref s.Get<T>(e);
        value = default;
        s.Remove<T>(e); s.Remove<T>(e);
        Require(!s.Has<T>(e), $"Remaining {typeof(T).Name} after Remove.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected an InvalidOperationException.");
    }
    private struct One : IProcessor<Identity>
    {
        public Dictionary<EntityKey, int> Expected;
        public HashSet<EntityKey> Seen;
        public int Count;
        public void Execute(EntityKey e, ref Identity a)
        {
            Require(Expected.TryGetValue(e, out var serial) && serial == a.Serial && Seen.Add(e), "One query returned an incorrect key.");
            a.Serial += 10; Count++;
        }
    }
    private struct Two : IProcessor<Identity, Motion>
    {
        public Dictionary<EntityKey, int> Expected;
        public HashSet<EntityKey> Seen;
        public int Count;
        public void Execute(EntityKey e, ref Identity a, ref Motion b)
        {
            Require(Expected.TryGetValue(e, out var serial) && serial == a.Serial && Seen.Add(e), "Two query returned an incorrect key.");
            a.Serial += 100; b.MaxSpeed += 2; Count++;
        }
    }
    private struct Three : IProcessor<Identity, Motion, Hull>
    {
        public EntityKey Expected;
        public int Count;
        public void Execute(EntityKey e, ref Identity a, ref Motion b, ref Hull c)
        {
            Require(e == Expected, "Three query returned an incorrect key.");
            b.MaxSpeed++; c.Current += 3; Count++;
        }
    }
    private struct Count : IProcessor<Identity>
    {
        public int Value;
        public void Execute(EntityKey e, ref Identity a) => Value++;
    }
    private struct Throws : IProcessor<Identity>
    {
        public void Execute(EntityKey e, ref Identity a) => throw new InvalidOperationException("Query unwind probe.");
    }
    private struct Mutation : IProcessor<Identity>
    {
        public IEcsStore Store;
        public int Count;
        public void Execute(EntityKey e, ref Identity a)
        {
            var store = Store;
            ExpectFailure(() => store.Create());
            Count++;
        }
    }
}
