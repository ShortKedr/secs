using System.Reflection;
using SpaceBattle;

internal static partial class Validation
{
    public static void SecsMappingChecks()
    {
        using (var adapter = new SecsStore()) AdapterValidation.Validate(adapter);
        SecsMappingGrowthChecks();
        SecsMappingReuseChecks();
        SecsMappingRetirementChecks();
        SecsMappingFallbackChecks();
        SecsMappingGuardChecks();
        Console.WriteLine("PASS: SECS reverse mapping, reuse, growth, bounded fallback and lifecycle guards.");
    }

    private static void SecsMappingGrowthChecks()
    {
        using var store = new SecsStore();
        var expected = new Dictionary<EntityKey, int>();
        var entities = new List<EntityKey>();
        for (var i = 0; i < 3; i++) entities.Add(SeedSecsMapping(store, expected, int.MaxValue - i));
        CheckSecsMappingQueries(store, expected);
        CheckSecsMappingQueries(store, expected);
        for (var i = 3; i < 600; i++) entities.Add(SeedSecsMapping(store, expected, int.MaxValue - i));
        CheckSecsMappingQueries(store, expected);
        Require(SecsReverseArray(store).Length >= 600, "SECS mapping grew across array boundaries");

        store.Set(entities[0], new MappingTag());
        CheckSecsMappingQueries(store, expected);
        store.Remove<MappingTag>(entities[0]);
        store.Remove<MappingOther>(entities[1]);
        Require(!store.Has<MappingOther>(entities[1]), "SECS migration removed component");
        store.Set(entities[1], new MappingOther { Value = 17 });
        CheckSecsMappingQueries(store, expected);
        foreach (var index in new[] { 0, 1, 300 })
        {
            store.Destroy(entities[index]);
            expected.Remove(entities[index]);
        }
        CheckSecsMappingQueries(store, expected);
        for (var i = 0; i < 3; i++) SeedSecsMapping(store, expected, -100 - i);
        CheckSecsMappingQueries(store, expected);
        Require(store.Diagnostics().Entities == expected.Count && store.Diagnostics().AccessorBytes == 0,
            "SECS mapping retains native storage and does not enable accessors");
    }

    private static void SecsMappingReuseChecks()
    {
        using var store = new SecsStore();
        var expected = new Dictionary<EntityKey, int>();
        var current = SeedSecsMapping(store, expected, 1);
        for (var i = 0; i < 256; i++)
        {
            CheckSecsMappingQueries(store, expected);
            var native = SecsMappingNative(store, current);
            ExpectSecsMappingFailure<InvalidOperationException>(() =>
                SecsQueryKey(store, new SECS.Entity(native.Id, checked(native.Version + 1))));
            Require(SecsQueryKey(store, native) == current, "SECS version mismatch did not poison cache");
            var stale = current;
            store.Destroy(stale);
            expected.Remove(stale);
            ExpectSecsMappingFailure<InvalidOperationException>(() => SecsQueryKey(store, native));
            current = SeedSecsMapping(store, expected, i + 100);
            Require(current.Id == stale.Id && current.Generation != stale.Generation, "SECS public generation reuse");
            Require(!store.Alive(stale) && !store.Has<MappingValue>(stale), "SECS stale key remains invalid");
            ExpectSecsMappingFailure<InvalidOperationException>(() => { _ = store.Get<MappingValue>(stale); });
            ExpectSecsMappingFailure<InvalidOperationException>(() => store.Set(stale, new MappingValue()));
            store.Destroy(stale);
            store.Remove<MappingValue>(stale);
            CheckSecsMappingQueries(store, expected);
        }
    }

    private static void SecsMappingRetirementChecks()
    {
        using var store = new SecsStore();
        var expected = new Dictionary<EntityKey, int>();
        var old = SeedSecsMapping(store, expected, 7);
        CheckSecsMappingQueries(store, expected);
        var native = SecsMappingNative(store, old);

        // Isolated fixture: reach public slot retirement without billions of destroy/create cycles.
        var slots = (Array)typeof(NativeStore<SECS.Entity>).GetField("slots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        var slot = slots.GetValue(old.Id)!;
        slot.GetType().GetField("Generation", BindingFlags.Instance | BindingFlags.Public)!.SetValue(slot, int.MaxValue);
        slots.SetValue(slot, old.Id);
        store.Destroy(new EntityKey(old.Id, int.MaxValue));
        expected.Remove(old);
        var replacement = SeedSecsMapping(store, expected, 8);
        var nextNative = SecsMappingNative(store, replacement);
        Require(replacement.Id != old.Id && nextNative.Id == native.Id,
            "SECS fixture separates public identity from reused native ID");
        CheckSecsMappingQueries(store, expected);
        Require(SecsQueryKey(store, nextNative) == replacement, "SECS mapping uses the full public identity");
        Require(!store.Alive(old), "SECS retired public key is stale");
    }

    private static void SecsMappingFallbackChecks()
    {
        using var store = new SecsStore();
        var cap = (int)typeof(SecsStore).GetField("MaxReverseEntries", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        Require(cap > 0 && (long)cap * 12 <= 1024 * 1024, "SECS dense metadata payload is bounded");
        for (var i = 0; i < cap - 1; i++) store.Create();
        var expected = new Dictionary<EntityKey, int>();
        var boundary = SeedSecsMapping(store, expected, -1);
        var overflow = SeedSecsMapping(store, expected, -2);
        var higher = SeedSecsMapping(store, expected, -3);
        Require(SecsMappingNative(store, boundary).Id == cap - 1 && SecsMappingNative(store, overflow).Id == cap,
            "SECS fixture reaches dense-cache boundary");
        CheckSecsMappingQueries(store, expected);
        CheckSecsMappingQueries(store, expected);
        Require(SecsReverseArray(store).Length == cap, "SECS fallback does not grow dense metadata beyond cap");
        var staleNative = SecsMappingNative(store, higher);
        store.Destroy(higher);
        expected.Remove(higher);
        var replacement = SeedSecsMapping(store, expected, -4);
        Require(SecsMappingNative(store, replacement).Id == staleNative.Id, "SECS fallback reuses native ID");
        ExpectSecsMappingFailure<InvalidOperationException>(() => SecsQueryKey(store, staleNative));
        CheckSecsMappingQueries(store, expected);
        ExpectSecsMappingFailure<InvalidOperationException>(() => SecsQueryKey(store, new SECS.Entity(int.MaxValue, 1)));
        ExpectSecsMappingFailure<InvalidOperationException>(() => SecsQueryKey(store, new SECS.Entity(-1, 1)));
        Require(SecsReverseArray(store).Length == cap, "SECS invalid huge IDs do not allocate metadata");
    }

    private static void SecsMappingGuardChecks()
    {
        using var store = new SecsStore();
        var expected = new Dictionary<EntityKey, int>();
        var e = SeedSecsMapping(store, expected, 9);
        CheckSecsMappingQueries(store, expected);
        var guard = new MappingGuard { Store = store, Expected = e };
        store.ForEach<MappingValue, MappingGuard>(ref guard);
        Require(guard.Count == 1 && store.Alive(e), "SECS nested query and rejected disposal preserve entity");
        var throwing = new MappingThrows();
        ExpectSecsMappingFailure<MappingQueryException>(() => store.ForEach<MappingValue, MappingThrows>(ref throwing));
        var extra = store.Create();
        store.Destroy(extra);
        store.Commit();
        CheckSecsMappingQueries(store, expected);
        store.Dispose();
        Require(SecsReverseArray(store).Length == 0, "SECS disposal releases reverse metadata");
        Require(!store.Alive(e) && !store.Has<MappingValue>(e), "SECS disposed store rejects liveness");
        ExpectSecsMappingFailure<ObjectDisposedException>(() => store.Create());
        ExpectSecsMappingFailure<ObjectDisposedException>(() => { _ = store.Get<MappingValue>(e); });
        ExpectSecsMappingFailure<ObjectDisposedException>(() => store.Set(e, new MappingValue()));
        ExpectSecsMappingFailure<ObjectDisposedException>(() => store.Remove<MappingValue>(e));
        ExpectSecsMappingFailure<ObjectDisposedException>(() => store.Destroy(e));
        ExpectSecsMappingFailure<ObjectDisposedException>(() => store.Commit());
        ExpectSecsMappingFailure<ObjectDisposedException>(() => store.Diagnostics());
        var one = new MappingOne();
        var two = new MappingTwo();
        var three = new MappingThree();
        ExpectSecsMappingFailure<ObjectDisposedException>(() => store.ForEach<MappingValue, MappingOne>(ref one));
        ExpectSecsMappingFailure<ObjectDisposedException>(() => store.ForEach<MappingValue, MappingOther, MappingTwo>(ref two));
        ExpectSecsMappingFailure<ObjectDisposedException>(() => store.ForEach<MappingValue, MappingOther, MappingThird, MappingThree>(ref three));
        store.Dispose();
    }

    private static EntityKey SeedSecsMapping(SecsStore store, Dictionary<EntityKey, int> expected, int value)
    {
        var e = store.Create();
        store.Set(e, new MappingValue { Value = value });
        store.Set(e, new MappingOther { Value = 17 });
        store.Set(e, new MappingThird { Value = 23 });
        expected.Add(e, value);
        return e;
    }

    private static void CheckSecsMappingQueries(SecsStore store, Dictionary<EntityKey, int> expected)
    {
        var one = new MappingOne { Expected = expected, Seen = new() };
        store.ForEach<MappingValue, MappingOne>(ref one);
        Require(one.Count == expected.Count && one.Seen.Count == expected.Count, "SECS one-component key membership");
        var two = new MappingTwo { Expected = expected, Seen = new() };
        store.ForEach<MappingValue, MappingOther, MappingTwo>(ref two);
        Require(two.Count == expected.Count && two.Seen.Count == expected.Count, "SECS two-component key membership");
        var three = new MappingThree { Expected = expected, Seen = new() };
        store.ForEach<MappingValue, MappingOther, MappingThird, MappingThree>(ref three);
        Require(three.Count == expected.Count && three.Seen.Count == expected.Count, "SECS three-component key membership");
        foreach (var pair in expected)
            Require(store.Get<MappingValue>(pair.Key).Value == pair.Value, "SECS query ref writes remain native");
    }

    private static void VisitSecsMapping(Dictionary<EntityKey, int> expected, HashSet<EntityKey> seen, EntityKey e, ref MappingValue value)
    {
        Require(expected.TryGetValue(e, out var number) && number == value.Value && seen.Add(e), "SECS query returns full current identity");
        value.Value = value.Value == int.MaxValue ? int.MinValue : checked(value.Value + 1);
        expected[e] = value.Value;
    }

    private static Array SecsReverseArray(SecsStore store) =>
        (Array)typeof(SecsStore).GetField("reverse", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;

    private static SECS.Entity SecsMappingNative(SecsStore store, EntityKey e) =>
        (SECS.Entity)typeof(NativeStore<SECS.Entity>).GetMethod("Native", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(store, new object[] { e })!;

    private static EntityKey SecsQueryKey(SecsStore store, SECS.Entity e)
    {
        try
        {
            return (EntityKey)typeof(SecsStore).GetMethod("QueryKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(store, new object[] { e })!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
        {
            throw new InvalidOperationException("SECS reverse lookup rejected invalid native identity.", exception.InnerException);
        }
    }

    private static void ExpectSecsMappingFailure<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name} in SECS mapping checks.");
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
        { VisitSecsMapping(Expected, Seen, e, ref value); Count++; }
    }

    private struct MappingTwo : IProcessor<MappingValue, MappingOther>
    {
        public Dictionary<EntityKey, int> Expected;
        public HashSet<EntityKey> Seen;
        public int Count;
        public void Execute(EntityKey e, ref MappingValue value, ref MappingOther other)
        { Require(other.Value == 17, "SECS second component survived structural change"); VisitSecsMapping(Expected, Seen, e, ref value); Count++; }
    }

    private struct MappingThree : IProcessor<MappingValue, MappingOther, MappingThird>
    {
        public Dictionary<EntityKey, int> Expected;
        public HashSet<EntityKey> Seen;
        public int Count;
        public void Execute(EntityKey e, ref MappingValue value, ref MappingOther other, ref MappingThird third)
        { Require(other.Value == 17 && third.Value == 23, "SECS query components survived structural change"); VisitSecsMapping(Expected, Seen, e, ref value); Count++; }
    }

    private struct MappingCount : IProcessor<MappingValue>
    {
        public EntityKey Expected;
        public int Count;
        public void Execute(EntityKey e, ref MappingValue value)
        { Require(e == Expected, "SECS nested query current identity"); Count++; }
    }

    private struct MappingGuard : IProcessor<MappingValue>
    {
        public SecsStore Store;
        public EntityKey Expected;
        public int Count;
        public void Execute(EntityKey e, ref MappingValue value)
        {
            Require(e == Expected, "SECS guarded query identity");
            var store = Store;
            ExpectSecsMappingFailure<InvalidOperationException>(() => store.Create());
            ExpectSecsMappingFailure<InvalidOperationException>(() => store.Destroy(e));
            ExpectSecsMappingFailure<InvalidOperationException>(() => store.Set(e, new MappingValue()));
            ExpectSecsMappingFailure<InvalidOperationException>(() => store.Remove<MappingValue>(e));
            ExpectSecsMappingFailure<InvalidOperationException>(() => store.Commit());
            ExpectSecsMappingFailure<InvalidOperationException>(() => store.Dispose());
            var nested = new MappingCount { Expected = e };
            store.ForEach<MappingValue, MappingCount>(ref nested);
            Require(nested.Count == 1, "SECS nested query membership");
            Count++;
        }
    }

    private struct MappingThrows : IProcessor<MappingValue>
    {
        public void Execute(EntityKey e, ref MappingValue value) => throw new MappingQueryException();
    }
}
