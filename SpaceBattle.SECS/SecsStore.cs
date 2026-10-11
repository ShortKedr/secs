namespace SpaceBattle;

public sealed class SecsStore : NativeStore<SECS.Entity>
{
    private readonly SECS.World world = new();
    private readonly Dictionary<Type, object> queries = new();
    public override string Name => "SECS";
    protected override SECS.Entity CreateNative() => world.CreateEntity();
    protected override bool AliveNative(SECS.Entity e) => world.IsAlive(e);
    protected override void DestroyNative(SECS.Entity e) => world.DestroyEntity(e);
    protected override void DisposeNative() { queries.Clear(); world.Dispose(); }
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
            ((SECS.Query<T>)cached).ForEach(ref bridge); p = bridge.Processor;
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
            ((SECS.Query<A, B>)cached).ForEach(ref bridge); p = bridge.Processor;
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
            ((SECS.Query<A, B, C>)cached).ForEach(ref bridge); p = bridge.Processor;
        }
        finally { EndQuery(); }
    }
    private struct Bridge<T, P> : SECS.IQueryProcessor<T> where T : unmanaged where P : struct, IProcessor<T>
    { public P Processor; public SecsStore Store; public void Execute(SECS.Entity e, ref T a) => Processor.Execute(Store.Key(e), ref a); }
    private struct Bridge<A, B, P> : SECS.IQueryProcessor<A, B> where A : unmanaged where B : unmanaged where P : struct, IProcessor<A, B>
    { public P Processor; public SecsStore Store; public void Execute(SECS.Entity e, ref A a, ref B b) => Processor.Execute(Store.Key(e), ref a, ref b); }
    private struct Bridge<A, B, C, P> : SECS.IQueryProcessor<A, B, C> where A : unmanaged where B : unmanaged where C : unmanaged where P : struct, IProcessor<A, B, C>
    { public P Processor; public SecsStore Store; public void Execute(SECS.Entity e, ref A a, ref B b, ref C c) => Processor.Execute(Store.Key(e), ref a, ref b, ref c); }
}
