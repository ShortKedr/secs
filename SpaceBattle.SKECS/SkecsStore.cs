namespace SpaceBattle;

public sealed class SkecsStore : NativeStore<SKECS.Entity>
{
    private readonly SKECS.World world = new();
    private readonly Dictionary<Type, object> queries = new();
    // Three ints per entry: bound the optional cache to 768 KiB of payload.
    private const int MaxReverseEntries = 65_536;
    private ReverseEntry[] reverse = Array.Empty<ReverseEntry>();

    private struct ReverseEntry
    {
        public int Version;
        public EntityKey Key;
    }

    public override string Name => "SKECS";
    protected override SKECS.Entity CreateNative()
    {
        var e = world.CreateEntity();
        if ((uint)e.Id < (uint)reverse.Length) reverse[e.Id] = default;
        return e;
    }
    protected override bool AliveNative(SKECS.Entity e) => world.IsAlive(e);
    protected override void DestroyNative(SKECS.Entity e)
    {
        world.DestroyEntity(e);
        if ((uint)e.Id < (uint)reverse.Length) reverse[e.Id] = default;
    }
    protected override void DisposeNative() { queries.Clear(); world.Dispose(); reverse = Array.Empty<ReverseEntry>(); }
    public override ref T Get<T>(EntityKey e) => ref world.Get<T>(Native(e));
    public override bool Has<T>(EntityKey e) => Alive(e) && world.Has<T>(Native(e));
    public override void Set<T>(EntityKey e, in T value) { CheckMutation(); world.Set(Native(e), value); }
    public override void Remove<T>(EntityKey e) { CheckMutation(); if (Alive(e)) world.Remove<T>(Native(e)); }
    public override StoreDiagnostics Diagnostics()
    {
        var d = world.GetStorageStatistics();
        return new(d.EntityCount, d.ArchetypeCount, d.ChunkCount, d.NativeBytes, d.AccessorBufferBytes);
    }
    public override void ForEach<T, P>(ref P p)
    {
        BeginQuery();
        try
        {
            var key = typeof(ValueTuple<T>);
            if (!queries.TryGetValue(key, out var cached)) queries.Add(key, cached = world.Query<T>());
            var bridge = new Bridge<T, P> { Processor = p, Store = this };
            ((SKECS.Query<T>)cached).ForEach(ref bridge); p = bridge.Processor;
        }
        finally { EndQuery(); }
    }
    public override void ForEach<A, B, P>(ref P p)
    {
        BeginQuery();
        try
        {
            var key = typeof(ValueTuple<A, B>);
            if (!queries.TryGetValue(key, out var cached)) queries.Add(key, cached = world.Query<A, B>());
            var bridge = new Bridge<A, B, P> { Processor = p, Store = this };
            ((SKECS.Query<A, B>)cached).ForEach(ref bridge); p = bridge.Processor;
        }
        finally { EndQuery(); }
    }
    public override void ForEach<A, B, C, P>(ref P p)
    {
        BeginQuery();
        try
        {
            var key = typeof(ValueTuple<A, B, C>);
            if (!queries.TryGetValue(key, out var cached)) queries.Add(key, cached = world.Query<A, B, C>());
            var bridge = new Bridge<A, B, C, P> { Processor = p, Store = this };
            ((SKECS.Query<A, B, C>)cached).ForEach(ref bridge); p = bridge.Processor;
        }
        finally { EndQuery(); }
    }
    public override unsafe void ForEachComponents<A, B, C, P>(ref P processor)
    {
        BeginQuery();
        try
        {
            var key = typeof(ValueTuple<A, B, C>);
            if (!queries.TryGetValue(key, out var cached)) queries.Add(key, cached = world.Query<A, B, C>());
            foreach (var chunk in ((SKECS.Query<A, B, C>)cached).Chunks())
            {
                // Native chunk columns stay allocated: BeginQuery forbids all structural mutation/disposal.
                var count = chunk.Count;
                var first = new Span<A>(chunk.Component1, count);
                var second = new Span<B>(chunk.Component2, count);
                var third = new Span<C>(chunk.Component3, count);
                for (var row = 0; row < count; row++)
                    processor.Execute(ref first[row], ref second[row], ref third[row]);
            }
        }
        finally { EndQuery(); }
    }
    private EntityKey QueryKey(SKECS.Entity e)
    {
        if ((uint)e.Id >= (uint)MaxReverseEntries) return Key(e);
        if ((uint)e.Id < (uint)reverse.Length)
        {
            ref var entry = ref reverse[e.Id];
            if (entry.Key.Generation != 0 && entry.Version == e.Version) return entry.Key;
        }
        // CreateNative runs before public identity registration; resolve only after publication.
        var key = Key(e);
        if (e.Id >= reverse.Length)
        {
            var capacity = Math.Min(MaxReverseEntries,
                Math.Max(checked(e.Id + 1), Math.Max(64, checked(reverse.Length * 2))));
            Array.Resize(ref reverse, capacity);
        }
        reverse[e.Id] = new ReverseEntry { Version = e.Version, Key = key };
        return key;
    }

    private struct Bridge<T, P> : SKECS.IQueryProcessor<T> where T : unmanaged where P : struct, IProcessor<T>
    { public P Processor; public SkecsStore Store; public void Execute(SKECS.Entity e, ref T a) => Processor.Execute(Store.QueryKey(e), ref a); }
    private struct Bridge<A, B, P> : SKECS.IQueryProcessor<A, B> where A : unmanaged where B : unmanaged where P : struct, IProcessor<A, B>
    { public P Processor; public SkecsStore Store; public void Execute(SKECS.Entity e, ref A a, ref B b) => Processor.Execute(Store.QueryKey(e), ref a, ref b); }
    private struct Bridge<A, B, C, P> : SKECS.IQueryProcessor<A, B, C> where A : unmanaged where B : unmanaged where C : unmanaged where P : struct, IProcessor<A, B, C>
    { public P Processor; public SkecsStore Store; public void Execute(SKECS.Entity e, ref A a, ref B b, ref C c) => Processor.Execute(Store.QueryKey(e), ref a, ref b, ref c); }
}
