using System.Reflection;
using SpaceBattle;

internal static partial class Validation
{
    public static void SkecsMappingChecks()
    {
        using (var adapter = new SkecsStore()) AdapterValidation.Validate(adapter);
        SkecsMappingGrowthChecks();
        SkecsMappingReuseChecks();
        SkecsMappingRetirementChecks();
        SkecsMappingFallbackChecks();
        SkecsMappingGuardChecks();
        Console.WriteLine("PASS: SKECS reverse mapping, reuse, growth, bounded fallback and lifecycle guards.");
    }

    private static void SkecsMappingGrowthChecks()
    {
        using var store = new SkecsStore();
        var expected = new Dictionary<EntityKey, int>();
        var entities = new List<EntityKey>();
        for (var i = 0; i < 3; i++) entities.Add(SeedSkecsMapping(store, expected, int.MaxValue - i));
        CheckSkecsMappingQueries(store, expected);
        CheckSkecsMappingQueries(store, expected);
        for (var i = 3; i < 600; i++) entities.Add(SeedSkecsMapping(store, expected, int.MaxValue - i));
        CheckSkecsMappingQueries(store, expected);
        Require(SkecsReverseArray(store).Length >= 600, "SKECS mapping grew across array boundaries");

        store.Set(entities[0], new MappingTag());
        CheckSkecsMappingQueries(store, expected);
        store.Remove<MappingTag>(entities[0]);
        store.Remove<MappingOther>(entities[1]);
        Require(!store.Has<MappingOther>(entities[1]), "SKECS migration removed component");
        store.Set(entities[1], new MappingOther { Value = 17 });
        CheckSkecsMappingQueries(store, expected);
        foreach (var index in new[] { 0, 1, 300 })
        {
            store.Destroy(entities[index]);
            expected.Remove(entities[index]);
        }
        CheckSkecsMappingQueries(store, expected);
        for (var i = 0; i < 3; i++) SeedSkecsMapping(store, expected, -100 - i);
        CheckSkecsMappingQueries(store, expected);
        Require(store.Diagnostics().Entities == expected.Count && store.Diagnostics().AccessorBytes == 0,
            "SKECS mapping retains native storage and does not enable accessors");
    }

    private static void SkecsMappingReuseChecks()
    {
        using var store = new SkecsStore();
        var expected = new Dictionary<EntityKey, int>();
        var current = SeedSkecsMapping(store, expected, 1);
        for (var i = 0; i < 256; i++)
        {
            CheckSkecsMappingQueries(store, expected);
            var native = SkecsMappingNative(store, current);
            ExpectSkecsMappingFailure<InvalidOperationException>(() =>
                SkecsQueryKey(store, new SKECS.Entity(native.Id, checked(native.Version + 1))));
            Require(SkecsQueryKey(store, native) == current, "SKECS version mismatch did not poison cache");
            var stale = current;
            store.Destroy(stale);
            expected.Remove(stale);
            ExpectSkecsMappingFailure<InvalidOperationException>(() => SkecsQueryKey(store, native));
            current = SeedSkecsMapping(store, expected, i + 100);
            Require(current.Id == stale.Id && current.Generation != stale.Generation, "SKECS public generation reuse");
            Require(!store.Alive(stale) && !store.Has<MappingValue>(stale), "SKECS stale key remains invalid");
            ExpectSkecsMappingFailure<InvalidOperationException>(() => { _ = store.Get<MappingValue>(stale); });
            ExpectSkecsMappingFailure<InvalidOperationException>(() => store.Set(stale, new MappingValue()));
            store.Destroy(stale);
            store.Remove<MappingValue>(stale);
            CheckSkecsMappingQueries(store, expected);
        }
    }

    private static void SkecsMappingRetirementChecks()
    {
        using var store = new SkecsStore();
        var expected = new Dictionary<EntityKey, int>();
        var old = SeedSkecsMapping(store, expected, 7);
        CheckSkecsMappingQueries(store, expected);
        var native = SkecsMappingNative(store, old);

        // Isolated fixture: reach public slot retirement without billions of destroy/create cycles.
        var slots = (Array)typeof(NativeStore<SKECS.Entity>).GetField("slots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        var slot = slots.GetValue(old.Id)!;
        slot.GetType().GetField("Generation", BindingFlags.Instance | BindingFlags.Public)!.SetValue(slot, int.MaxValue);
        slots.SetValue(slot, old.Id);
        store.Destroy(new EntityKey(old.Id, int.MaxValue));
        expected.Remove(old);
        var replacement = SeedSkecsMapping(store, expected, 8);
        var nextNative = SkecsMappingNative(store, replacement);
        Require(replacement.Id != old.Id && nextNative.Id == native.Id,
            "SKECS fixture separates public identity from reused native ID");
        CheckSkecsMappingQueries(store, expected);
        Require(SkecsQueryKey(store, nextNative) == replacement, "SKECS mapping uses the full public identity");
        Require(!store.Alive(old), "SKECS retired public key is stale");
    }

    private static void SkecsMappingFallbackChecks()
    {
        using var store = new SkecsStore();
        var cap = (int)typeof(SkecsStore).GetField("MaxReverseEntries", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        Require(cap > 0 && (long)cap * 12 <= 1024 * 1024, "SKECS dense metadata payload is bounded");
        for (var i = 0; i < cap - 1; i++) store.Create();
        var expected = new Dictionary<EntityKey, int>();
        var boundary = SeedSkecsMapping(store, expected, -1);
        var overflow = SeedSkecsMapping(store, expected, -2);
        var higher = SeedSkecsMapping(store, expected, -3);
        Require(SkecsMappingNative(store, boundary).Id == cap - 1 && SkecsMappingNative(store, overflow).Id == cap,
            "SKECS fixture reaches dense-cache boundary");
        CheckSkecsMappingQueries(store, expected);
        CheckSkecsMappingQueries(store, expected);
        Require(SkecsReverseArray(store).Length == cap, "SKECS fallback does not grow dense metadata beyond cap");
        var staleNative = SkecsMappingNative(store, higher);
        store.Destroy(higher);
        expected.Remove(higher);
        var replacement = SeedSkecsMapping(store, expected, -4);
        Require(SkecsMappingNative(store, replacement).Id == staleNative.Id, "SKECS fallback reuses native ID");
        ExpectSkecsMappingFailure<InvalidOperationException>(() => SkecsQueryKey(store, staleNative));
        CheckSkecsMappingQueries(store, expected);
        ExpectSkecsMappingFailure<InvalidOperationException>(() => SkecsQueryKey(store, new SKECS.Entity(int.MaxValue, 1)));
        ExpectSkecsMappingFailure<InvalidOperationException>(() => SkecsQueryKey(store, new SKECS.Entity(-1, 1)));
        Require(SkecsReverseArray(store).Length == cap, "SKECS invalid huge IDs do not allocate metadata");
    }

    private static void SkecsMappingGuardChecks()
    {
        using var store = new SkecsStore();
        var expected = new Dictionary<EntityKey, int>();
        var e = SeedSkecsMapping(store, expected, 9);
        CheckSkecsMappingQueries(store, expected);
        var guard = new MappingGuard { Store = store, Expected = e };
        store.ForEach<MappingValue, MappingGuard>(ref guard);
        Require(guard.Count == 1 && store.Alive(e), "SKECS nested query and rejected disposal preserve entity");
        var throwing = new MappingThrows();
        ExpectSkecsMappingFailure<MappingQueryException>(() => store.ForEach<MappingValue, MappingThrows>(ref throwing));
        var extra = store.Create();
        store.Destroy(extra);
        store.Commit();
        CheckSkecsMappingQueries(store, expected);
        store.Dispose();
        Require(SkecsReverseArray(store).Length == 0, "SKECS disposal releases reverse metadata");
        Require(!store.Alive(e) && !store.Has<MappingValue>(e), "SKECS disposed store rejects liveness");
        ExpectSkecsMappingFailure<ObjectDisposedException>(() => store.Create());
        ExpectSkecsMappingFailure<ObjectDisposedException>(() => { _ = store.Get<MappingValue>(e); });
        ExpectSkecsMappingFailure<ObjectDisposedException>(() => store.Set(e, new MappingValue()));
        ExpectSkecsMappingFailure<ObjectDisposedException>(() => store.Remove<MappingValue>(e));
        ExpectSkecsMappingFailure<ObjectDisposedException>(() => store.Destroy(e));
        ExpectSkecsMappingFailure<ObjectDisposedException>(() => store.Commit());
        ExpectSkecsMappingFailure<ObjectDisposedException>(() => store.Diagnostics());
        var one = new MappingOne();
        var two = new MappingTwo();
        var three = new MappingThree();
        ExpectSkecsMappingFailure<ObjectDisposedException>(() => store.ForEach<MappingValue, MappingOne>(ref one));
        ExpectSkecsMappingFailure<ObjectDisposedException>(() => store.ForEach<MappingValue, MappingOther, MappingTwo>(ref two));
        ExpectSkecsMappingFailure<ObjectDisposedException>(() => store.ForEach<MappingValue, MappingOther, MappingThird, MappingThree>(ref three));
        store.Dispose();
    }

    private static EntityKey SeedSkecsMapping(SkecsStore store, Dictionary<EntityKey, int> expected, int value)
    {
        var e = store.Create();
        store.Set(e, new MappingValue { Value = value });
        store.Set(e, new MappingOther { Value = 17 });
        store.Set(e, new MappingThird { Value = 23 });
        expected.Add(e, value);
        return e;
    }

    private static void CheckSkecsMappingQueries(SkecsStore store, Dictionary<EntityKey, int> expected)
    {
        var one = new MappingOne { Expected = expected, Seen = new() };
        store.ForEach<MappingValue, MappingOne>(ref one);
        Require(one.Count == expected.Count && one.Seen.Count == expected.Count, "SKECS one-component key membership");
        var two = new MappingTwo { Expected = expected, Seen = new() };
        store.ForEach<MappingValue, MappingOther, MappingTwo>(ref two);
        Require(two.Count == expected.Count && two.Seen.Count == expected.Count, "SKECS two-component key membership");
        var three = new MappingThree { Expected = expected, Seen = new() };
        store.ForEach<MappingValue, MappingOther, MappingThird, MappingThree>(ref three);
        Require(three.Count == expected.Count && three.Seen.Count == expected.Count, "SKECS three-component key membership");
        foreach (var pair in expected)
            Require(store.Get<MappingValue>(pair.Key).Value == pair.Value, "SKECS query ref writes remain native");
    }

    private static void VisitSkecsMapping(Dictionary<EntityKey, int> expected, HashSet<EntityKey> seen, EntityKey e, ref MappingValue value)
    {
        Require(expected.TryGetValue(e, out var number) && number == value.Value && seen.Add(e), "SKECS query returns full current identity");
        value.Value = value.Value == int.MaxValue ? int.MinValue : checked(value.Value + 1);
        expected[e] = value.Value;
    }

    private static Array SkecsReverseArray(SkecsStore store) =>
        (Array)typeof(SkecsStore).GetField("reverse", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;

    private static SKECS.Entity SkecsMappingNative(SkecsStore store, EntityKey e) =>
        (SKECS.Entity)typeof(NativeStore<SKECS.Entity>).GetMethod("Native", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(store, new object[] { e })!;

    private static EntityKey SkecsQueryKey(SkecsStore store, SKECS.Entity e)
    {
        try
        {
            return (EntityKey)typeof(SkecsStore).GetMethod("QueryKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(store, new object[] { e })!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
        {
            throw new InvalidOperationException("SKECS reverse lookup rejected invalid native identity.", exception.InnerException);
        }
    }

    private static void ExpectSkecsMappingFailure<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name} in SKECS mapping checks.");
    }

    private struct MappingValue { public int Value; }
    private struct MappingOther { public int Value; }
    private struct MappingThird { public int Value; }
    private struct MappingTag { }
    private sealed class MappingQueryException : Exception { }

    private struct MappingOne : IProcessor<MappingValue>
    {
        public Dictionary<EntityKey, int> Expected;
        public HashSet<EntityKey> Seen;
        public int Count;
        public void Execute(EntityKey e, ref MappingValue value)
        { VisitSkecsMapping(Expected, Seen, e, ref value); Count++; }
    }

    private struct MappingTwo : IProcessor<MappingValue, MappingOther>
    {
        public Dictionary<EntityKey, int> Expected;
        public HashSet<EntityKey> Seen;
        public int Count;
        public void Execute(EntityKey e, ref MappingValue value, ref MappingOther other)
        { Require(other.Value == 17, "SKECS second component survived structural change"); VisitSkecsMapping(Expected, Seen, e, ref value); Count++; }
    }

    private struct MappingThree : IProcessor<MappingValue, MappingOther, MappingThird>
    {
        public Dictionary<EntityKey, int> Expected;
        public HashSet<EntityKey> Seen;
        public int Count;
        public void Execute(EntityKey e, ref MappingValue value, ref MappingOther other, ref MappingThird third)
        { Require(other.Value == 17 && third.Value == 23, "SKECS query components survived structural change"); VisitSkecsMapping(Expected, Seen, e, ref value); Count++; }
    }

    private struct MappingCount : IProcessor<MappingValue>
    {
        public EntityKey Expected;
        public int Count;
        public void Execute(EntityKey e, ref MappingValue value)
        { Require(e == Expected, "SKECS nested query current identity"); Count++; }
    }

    private struct MappingGuard : IProcessor<MappingValue>
    {
        public SkecsStore Store;
        public EntityKey Expected;
        public int Count;
        public void Execute(EntityKey e, ref MappingValue value)
        {
            Require(e == Expected, "SKECS guarded query identity");
            var store = Store;
            ExpectSkecsMappingFailure<InvalidOperationException>(() => store.Create());
            ExpectSkecsMappingFailure<InvalidOperationException>(() => store.Destroy(e));
            ExpectSkecsMappingFailure<InvalidOperationException>(() => store.Set(e, new MappingValue()));
            ExpectSkecsMappingFailure<InvalidOperationException>(() => store.Remove<MappingValue>(e));
            ExpectSkecsMappingFailure<InvalidOperationException>(() => store.Commit());
            ExpectSkecsMappingFailure<InvalidOperationException>(() => store.Dispose());
            var nested = new MappingCount { Expected = e };
            store.ForEach<MappingValue, MappingCount>(ref nested);
            Require(nested.Count == 1, "SKECS nested query membership");
            Count++;
        }
    }

    private struct MappingThrows : IProcessor<MappingValue>
    {
        public void Execute(EntityKey e, ref MappingValue value) => throw new MappingQueryException();
    }
}
