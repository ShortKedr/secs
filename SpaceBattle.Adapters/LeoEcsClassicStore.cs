using Leopotam.Ecs;

namespace SpaceBattle;

public sealed class LeoEcsClassicStore : NativeStore<EcsEntity>
{
    private struct LifetimeMarker { public byte Value; }
    private readonly EcsWorld world = new();
    public override string Name => "LeoECS Classic";
    protected override EcsEntity CreateNative()
    {
        var e = world.NewEntity();
        e.Get<LifetimeMarker>().Value = 1;
        return e;
    }
    protected override bool AliveNative(EcsEntity e) => e.IsAlive();
    protected override void DestroyNative(EcsEntity e) => e.Destroy();
    protected override void DisposeNative() => world.Destroy();
    public override ref T Get<T>(EntityKey e)
    {
        var native = Native(e);
        if (!native.Has<T>()) throw new InvalidOperationException("Component is absent.");
        var index = native.GetComponentIndexInPool<T>();
        return ref world.GetPool<T>().Items[index];
    }
    public override bool Has<T>(EntityKey e) => Alive(e) && Native(e).Has<T>();
    public override void Set<T>(EntityKey e, in T value)
    {
        CheckMutation();
        Native(e).Get<T>() = value;
    }
    public override void Remove<T>(EntityKey e)
    {
        CheckMutation();
        if (Alive(e)) Native(e).Del<T>();
    }
    public override void ForEach<A, P>(ref P p)
    {
        BeginQuery();
        try
        {
            var filter = (EcsFilter<A>)world.GetFilter(typeof(EcsFilter<A>));
            foreach (var i in filter)
                p.Execute(Key(filter.GetEntity(i)), ref filter.Get1(i));
        }
        finally { EndQuery(); }
    }
    public override void ForEach<A, B, P>(ref P p)
    {
        BeginQuery();
        try
        {
            var filter = (EcsFilter<A, B>)world.GetFilter(typeof(EcsFilter<A, B>));
            foreach (var i in filter)
                p.Execute(Key(filter.GetEntity(i)), ref filter.Get1(i), ref filter.Get2(i));
        }
        finally { EndQuery(); }
    }
    public override void ForEach<A, B, C, P>(ref P p)
    {
        BeginQuery();
        try
        {
            var filter = (EcsFilter<A, B, C>)world.GetFilter(typeof(EcsFilter<A, B, C>));
            foreach (var i in filter)
                p.Execute(Key(filter.GetEntity(i)), ref filter.Get1(i), ref filter.Get2(i), ref filter.Get3(i));
        }
        finally { EndQuery(); }
    }
}
