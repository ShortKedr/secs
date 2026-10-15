using SpaceBattle;

internal static partial class Validation
{
    public static void ComponentPassChecks()
    {
        var vectorized = System.Numerics.Vector.IsHardwareAccelerated;
        var vector128 = System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated;
        if (Environment.GetEnvironmentVariable("DOTNET_EnableHWIntrinsic") == "0")
            Require(!vectorized && !vector128, "forced scalar runtime gate is active");
        Console.WriteLine($"Runtime SIMD gates: Vector={vectorized}, Vector128={vector128}");
        foreach (var factory in new Func<IEcsStore>[]
        {
            static () => new SkecsStore(), static () => new LeoEcsLiteStore(),
            static () => new LeoEcsClassicStore(), static () => new MorpehStore(),
            static () => new StaticEcsStore(r => { r.Register<PassA>(); r.Register<PassB>(); r.Register<PassC>(); r.Register<PassTag>(); })
        })
        {
            using var store = factory();
            var keys = new List<EntityKey>();
            foreach (var size in new[] { 0, 1, 3, 7, 8, 9, 17, 65, 513, 4099 })
            {
                while (keys.Count < size) keys.Add(SeedComponentRow(store, keys.Count));
                store.Commit();
                CompareComponentRows(store, size);
            }
            store.Set(keys[0], new PassTag());
            store.Remove<PassTag>(keys[0]);
            store.Get<PassA>(keys[1]).Value = -81;
            var stale = keys[2]; store.Destroy(stale);
            keys[2] = SeedComponentRow(store, 900);
            store.Commit();
            Require(!store.Alive(stale) && keys[2].Generation != stale.Generation,
                store.Name + " component-only path preserves slot generations");
            CompareComponentRows(store, keys.Count);
            var guard = new PassGuard { Store = store, Key = keys[0] };
            store.ForEachComponents<PassA, PassB, PassC, PassGuard>(ref guard);
            Require(guard.Count == keys.Count, store.Name + " component guards and nested query");
            var throwing = new PassThrow();
            ExpectComponentFailure<PassException>(() => store.ForEachComponents<PassA, PassB, PassC, PassThrow>(ref throwing));
            var extra = SeedComponentRow(store, 901); store.Destroy(extra); store.Commit();
            CompareComponentRows(store, keys.Count);

            // Query creation and all storage growth are outside the allocation measurement.
            var sum = new PassSum();
            store.ForEachComponents<PassA, PassB, PassC, PassSum>(ref sum);
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 20; i++) store.ForEachComponents<PassA, PassB, PassC, PassSum>(ref sum);
            Require(GC.GetAllocatedBytesForCurrentThread() == bytes, store.Name + " warmed component pass allocates nothing");
            store.Dispose();
            ExpectComponentFailure<ObjectDisposedException>(() => store.ForEachComponents<PassA, PassB, PassC, PassSum>(ref sum));
            Console.WriteLine("PASS: " + store.Name + " component-only order/ref writes, empty/tails, reuse, mutation/disposal guards and allocations.");
        }
    }

    private static EntityKey SeedComponentRow(IEcsStore store, int value)
    {
        var key = store.Create();
        store.Set(key, new PassA { Value = value }); store.Set(key, new PassB { Value = value + 1 });
        store.Set(key, new PassC { Value = value + 2 });
        if ((value & 1) != 0) store.Set(key, new PassTag());
        return key;
    }

    private static void CompareComponentRows(IEcsStore store, int expected)
    {
        var keyed = new PassKeyed { Rows = new() };
        store.ForEach<PassA, PassB, PassC, PassKeyed>(ref keyed);
        var components = new PassComponents { Rows = new() };
        store.ForEachComponents<PassA, PassB, PassC, PassComponents>(ref components);
        Require(keyed.Rows.Count == expected && keyed.Rows.SequenceEqual(components.Rows), store.Name + " same query order and values");
        var writes = new PassWrite(); store.ForEachComponents<PassA, PassB, PassC, PassWrite>(ref writes);
        var after = new PassKeyed { Rows = new() }; store.ForEach<PassA, PassB, PassC, PassKeyed>(ref after);
        for (var i = 0; i < after.Rows.Count; i++)
        {
            var old = keyed.Rows[i];
            Require(after.Rows[i] == (old.Item1 + 1, old.Item2 - 2, old.Item3 + 3), store.Name + " native component references");
        }
    }

    private static void ExpectComponentFailure<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private struct PassA { public int Value; }
    private struct PassB { public int Value; }
    private struct PassC { public int Value; }
    private struct PassTag { }
    private struct PassKeyed : IProcessor<PassA, PassB, PassC>
    {
        public List<(int, int, int)> Rows;
        public void Execute(EntityKey key, ref PassA a, ref PassB b, ref PassC c) => Rows.Add((a.Value, b.Value, c.Value));
    }
    private struct PassComponents : IComponentProcessor<PassA, PassB, PassC>
    {
        public List<(int, int, int)> Rows;
        public void Execute(ref PassA a, ref PassB b, ref PassC c) => Rows.Add((a.Value, b.Value, c.Value));
    }
    private struct PassWrite : IComponentProcessor<PassA, PassB, PassC>
    {
        public void Execute(ref PassA a, ref PassB b, ref PassC c) { a.Value++; b.Value -= 2; c.Value += 3; }
    }
    private struct PassSum : IComponentProcessor<PassA, PassB, PassC>
    {
        public long Value;
        public void Execute(ref PassA a, ref PassB b, ref PassC c) => Value += a.Value + b.Value + c.Value;
    }
    private struct PassGuard : IComponentProcessor<PassA, PassB, PassC>
    {
        public IEcsStore Store;
        public EntityKey Key;
        public int Count;
        public void Execute(ref PassA a, ref PassB b, ref PassC c)
        {
            if (Count++ != 0) return;
            var store = Store; var key = Key;
            ExpectComponentFailure<InvalidOperationException>(() => store.Create());
            ExpectComponentFailure<InvalidOperationException>(() => store.Destroy(key));
            ExpectComponentFailure<InvalidOperationException>(() => store.Set(key, new PassA()));
            ExpectComponentFailure<InvalidOperationException>(() => store.Remove<PassA>(key));
            ExpectComponentFailure<InvalidOperationException>(() => store.Commit());
            ExpectComponentFailure<InvalidOperationException>(() => store.Dispose());
            var nested = new PassSum(); Store.ForEachComponents<PassA, PassB, PassC, PassSum>(ref nested);
        }
    }
    private sealed class PassException : Exception { }
    private struct PassThrow : IComponentProcessor<PassA, PassB, PassC>
    {
        public void Execute(ref PassA a, ref PassB b, ref PassC c) => throw new PassException();
    }
}
