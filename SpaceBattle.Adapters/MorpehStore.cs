using Scellecs.Morpeh;

namespace SpaceBattle;

public sealed class MorpehStore : NativeStore<Entity>
{
    private struct Wrapper<T> : IComponent where T : unmanaged { public T Value; }
    private struct LifetimeMarker : IComponent { public byte Value; }
    private readonly World world = World.Create();
    private readonly Dictionary<Type, Filter> filters = new();
    public override string Name => "Morpeh";
    protected override Entity CreateNative()
    {
        var e = world.CreateEntity();
        world.GetStash<LifetimeMarker>().Add(e).Value = 1;
        return e;
    }
    protected override bool AliveNative(Entity e) => !world.IsDisposed(e);
    protected override void DestroyNative(Entity e) => world.RemoveEntity(e);
    protected override void DisposeNative() { filters.Clear(); world.Dispose(); }
    public override ref T Get<T>(EntityKey e)
    {
        var native = Native(e);
        var stash = world.GetStash<Wrapper<T>>();
        if (!stash.Has(native)) throw new InvalidOperationException("Component is absent.");
        return ref stash.Get(native).Value;
    }
    public override bool Has<T>(EntityKey e) => Alive(e) && world.GetStash<Wrapper<T>>().Has(Native(e));
    public override void Set<T>(EntityKey e, in T value)
    {
        CheckMutation();
        world.GetStash<Wrapper<T>>().Set(Native(e), new Wrapper<T> { Value = value });
    }
    public override void Remove<T>(EntityKey e)
    {
        CheckMutation();
        if (Alive(e)) world.GetStash<Wrapper<T>>().Remove(Native(e));
    }
    public override void Commit() { CheckMutation(); world.Commit(); }
    public override void ForEach<A, P>(ref P p)
    {
        BeginQuery();
        try
        {
            var type = typeof(ValueTuple<A>);
            if (!filters.TryGetValue(type, out var filter))
            {
                filter = world.Filter.With<Wrapper<A>>().Build();
                filters.Add(type, filter);
            }
            var stashA = world.GetStash<Wrapper<A>>();
            foreach (var native in filter)
                p.Execute(Key(native), ref stashA.Get(native).Value);
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
                filter = world.Filter.With<Wrapper<A>>().With<Wrapper<B>>().Build();
                filters.Add(type, filter);
            }
            var stashA = world.GetStash<Wrapper<A>>();
            var stashB = world.GetStash<Wrapper<B>>();
            foreach (var native in filter)
                p.Execute(Key(native), ref stashA.Get(native).Value, ref stashB.Get(native).Value);
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
                filter = world.Filter.With<Wrapper<A>>().With<Wrapper<B>>().With<Wrapper<C>>().Build();
                filters.Add(type, filter);
            }
            var stashA = world.GetStash<Wrapper<A>>();
            var stashB = world.GetStash<Wrapper<B>>();
            var stashC = world.GetStash<Wrapper<C>>();
            foreach (var native in filter)
                p.Execute(Key(native), ref stashA.Get(native).Value, ref stashB.Get(native).Value, ref stashC.Get(native).Value);
        }
        finally { EndQuery(); }
    }
}
