using Leopotam.EcsLite;

namespace SpaceBattle;

public sealed class LeoEcsLiteStore : NativeStore<EcsPackedEntity>
{
    private sealed class PackedComparer : IEqualityComparer<EcsPackedEntity>
    {
        internal static readonly PackedComparer Instance = new();
        public bool Equals(EcsPackedEntity a, EcsPackedEntity b) => a.Id == b.Id && a.Gen == b.Gen;
        public int GetHashCode(EcsPackedEntity e) => e.GetHashCode();
    }
    public LeoEcsLiteStore() : base(PackedComparer.Instance) { }
    private struct LifetimeMarker { public byte Value; }
    private readonly EcsWorld world = new();
    private readonly Dictionary<Type, EcsFilter> filters = new();
    public override string Name => "LeoECSLite";
    protected override EcsPackedEntity CreateNative()
    {
        var id = world.NewEntity();
        world.GetPool<LifetimeMarker>().Add(id).Value = 1;
        return world.PackEntity(id);
    }
    protected override bool AliveNative(EcsPackedEntity e) => e.Unpack(world, out _);
    protected override void DestroyNative(EcsPackedEntity e) => world.DelEntity(e.Id);
    protected override void DisposeNative() { filters.Clear(); world.Destroy(); }
    public override ref T Get<T>(EntityKey e)
    {
        var id = Native(e).Id;
        var pool = world.GetPool<T>();
        if (!pool.Has(id)) throw new InvalidOperationException("Component is absent.");
        return ref pool.Get(id);
    }
    public override bool Has<T>(EntityKey e) => Alive(e) && world.GetPool<T>().Has(Native(e).Id);
    public override void Set<T>(EntityKey e, in T value)
    {
        CheckMutation();
        var id = Native(e).Id;
        var pool = world.GetPool<T>();
        if (pool.Has(id)) pool.Get(id) = value;
        else pool.Add(id) = value;
    }
    public override void Remove<T>(EntityKey e)
    {
        CheckMutation();
        if (Alive(e)) world.GetPool<T>().Del(Native(e).Id);
    }
    public override void ForEach<A, P>(ref P p)
    {
        BeginQuery();
        try
        {
            var type = typeof(ValueTuple<A>);
            if (!filters.TryGetValue(type, out var filter))
            {
                filter = world.Filter<A>().End();
                filters.Add(type, filter);
            }
            var poolA = world.GetPool<A>();
            foreach (var id in filter)
                p.Execute(Key(world.PackEntity(id)), ref poolA.Get(id));
        }
        finally { EndQuery(); }
    }
    public override void ForEach<A, B, P>(ref P p)
    {
        BeginQuery();
        try
        {
            var type = typeof(ValueTuple<A, B>);
            if (!filters.TryGetValue(type, out var filter))
            {
                filter = world.Filter<A>().Inc<B>().End();
                filters.Add(type, filter);
            }
            var poolA = world.GetPool<A>();
            var poolB = world.GetPool<B>();
            foreach (var id in filter)
                p.Execute(Key(world.PackEntity(id)), ref poolA.Get(id), ref poolB.Get(id));
        }
        finally { EndQuery(); }
    }
    public override void ForEach<A, B, C, P>(ref P p)
    {
        BeginQuery();
        try
        {
            var type = typeof(ValueTuple<A, B, C>);
            if (!filters.TryGetValue(type, out var filter))
            {
                filter = world.Filter<A>().Inc<B>().Inc<C>().End();
                filters.Add(type, filter);
            }
            var poolA = world.GetPool<A>();
            var poolB = world.GetPool<B>();
            var poolC = world.GetPool<C>();
            foreach (var id in filter)
                p.Execute(Key(world.PackEntity(id)), ref poolA.Get(id), ref poolB.Get(id), ref poolC.Get(id));
        }
        finally { EndQuery(); }
    }
}
