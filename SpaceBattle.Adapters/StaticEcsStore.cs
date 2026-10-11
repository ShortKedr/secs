using FFS.Libraries.StaticEcs;
using NativeWorld = FFS.Libraries.StaticEcs.World<SpaceBattle.SpaceBattleStaticWorld>;

namespace SpaceBattle;

internal readonly struct SpaceBattleStaticWorld : IWorldType { }

public sealed class StaticEcsStore : NativeStore<EntityGID>
{
    private struct Wrapper<T> : IComponent where T : unmanaged { public T Value; }
    private static int active;
    private readonly HashSet<Type> registered = new();

    // Additional unmanaged types can be registered here, before native Initialize.
    public sealed class ComponentRegistration
    {
        private bool open = true;
        private readonly HashSet<Type> registered;
        internal ComponentRegistration(HashSet<Type> registered) => this.registered = registered;
        public void Register<T>() where T : unmanaged
        {
            if (!open) throw new InvalidOperationException("StaticEcs registration has closed.");
            if (!registered.Add(typeof(T))) return;
            NativeWorld.RegisterComponentType<Wrapper<T>>();
        }
        internal void Close() => open = false;
    }

    public StaticEcsStore() : this(null) { }

    public StaticEcsStore(Action<ComponentRegistration>? registerAdditional)
    {
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
            throw new InvalidOperationException("Only one StaticEcsStore may be active; dispose it first.");
        bool created = false;
        var registration = new ComponentRegistration(registered);
        try
        {
            NativeWorld.Create(WorldConfig.Default());
            created = true;
            registration.Register<Identity>();
            registration.Register<Transform>();
            registration.Register<Motion>();
            registration.Register<Hull>();
            registration.Register<Shield>();
            registration.Register<Heat>();
            registration.Register<Brain>();
            registration.Register<Weapon>();
            registration.Register<SecondaryWeapon>();
            registration.Register<MountedTurret>();
            registration.Register<Collider>();
            registration.Register<PhysicsLink>();
            registration.Register<CombatState>();
            registration.Register<Carrier>();
            registration.Register<Docked>();
            registration.Register<Overheated>();
            registration.Register<InCombat>();
            registration.Register<Boosting>();
            registration.Register<ShieldBroken>();
            registration.Register<Projectile>();
            registration.Register<Debris>();
            registration.Register<Particle>();
            registration.Register<Lifetime>();
            registration.Register<InventoryItem>();
            registration.Register<InventoryItems>();
            registration.Register<Inventory>();
            registerAdditional?.Invoke(registration);
            registration.Close();
            NativeWorld.Initialize();
        }
        catch
        {
            try
            {
                if (created)
                {
                    // Native Destroy requires an initialized world, even on registration failure.
                    if (!NativeWorld.IsWorldInitialized()) NativeWorld.Initialize();
                    NativeWorld.Destroy();
                }
            }
            finally { Interlocked.Exchange(ref active, 0); }
            throw;
        }
        finally { registration.Close(); }
    }

    public override string Name => "StaticEcs 1.2.01";
    private void CheckType<T>() where T : unmanaged
    {
        CheckOpen();
        if (!registered.Contains(typeof(T)))
            throw new InvalidOperationException($"Register {typeof(T)} in the StaticEcsStore constructor before initialization.");
    }
    protected override EntityGID CreateNative() => NativeWorld.Entity.New().Gid();
    protected override bool AliveNative(EntityGID e) => e.TryUnpack<SpaceBattleStaticWorld>(out _);
    protected override void DestroyNative(EntityGID e) => e.Unpack<SpaceBattleStaticWorld>().Destroy();
    protected override void DisposeNative()
    {
        NativeWorld.Destroy();
        Interlocked.Exchange(ref active, 0);
    }
    public override ref T Get<T>(EntityKey e)
    {
        CheckType<T>();
        var native = Native(e).Unpack<SpaceBattleStaticWorld>();
        if (!native.HasAllOf<Wrapper<T>>()) throw new InvalidOperationException("Component is absent.");
        return ref native.Ref<Wrapper<T>>().Value;
    }
    public override bool Has<T>(EntityKey e)
    {
        if (!Alive(e)) return false;
        CheckType<T>();
        return Native(e).Unpack<SpaceBattleStaticWorld>().HasAllOf<Wrapper<T>>();
    }
    public override void Set<T>(EntityKey e, in T value)
    {
        CheckMutation();
        CheckType<T>();
        var native = Native(e).Unpack<SpaceBattleStaticWorld>();
        if (native.HasAllOf<Wrapper<T>>()) native.Ref<Wrapper<T>>().Value = value;
        else native.Add(new Wrapper<T> { Value = value });
    }
    public override void Remove<T>(EntityKey e)
    {
        CheckMutation();
        CheckType<T>();
        if (Alive(e))
        {
            var native = Native(e).Unpack<SpaceBattleStaticWorld>();
            if (native.HasAllOf<Wrapper<T>>()) native.Delete<Wrapper<T>>();
        }
    }
    public override void ForEach<A, P>(ref P p)
    {
        CheckType<A>();
        BeginQuery();
        try
        {
            foreach (var native in NativeWorld.Query.Entities<All<Wrapper<A>>>())
                p.Execute(Key(native.Gid()), ref native.Ref<Wrapper<A>>().Value);
        }
        finally { EndQuery(); }
    }
    public override void ForEach<A, B, P>(ref P p)
    {
        CheckType<A>(); CheckType<B>();
        BeginQuery();
        try
        {
            foreach (var native in NativeWorld.Query.Entities<All<Wrapper<A>, Wrapper<B>>>())
                p.Execute(Key(native.Gid()), ref native.Ref<Wrapper<A>>().Value, ref native.Ref<Wrapper<B>>().Value);
        }
        finally { EndQuery(); }
    }
    public override void ForEach<A, B, C, P>(ref P p)
    {
        CheckType<A>(); CheckType<B>(); CheckType<C>();
        BeginQuery();
        try
        {
            foreach (var native in NativeWorld.Query.Entities<All<Wrapper<A>, Wrapper<B>, Wrapper<C>>>())
                p.Execute(Key(native.Gid()), ref native.Ref<Wrapper<A>>().Value, ref native.Ref<Wrapper<B>>().Value, ref native.Ref<Wrapper<C>>().Value);
        }
        finally { EndQuery(); }
    }
}
