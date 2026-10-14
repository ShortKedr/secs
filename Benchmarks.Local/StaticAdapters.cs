using System;
using System.Runtime.InteropServices;
using FFS.Libraries.StaticEcs;
using SECS.Benchmarks;
using BW = FFS.Libraries.StaticEcs.World<StaticBasicWorld>;
using SW = FFS.Libraries.StaticEcs.World<StaticStressWorld>;

readonly struct StaticBasicWorld : IWorldType { }
readonly struct StaticStressWorld : IWorldType { }

sealed class StaticWorkload : IWorkload
{
    private struct P : IComponent { public float X, Y; }
    private struct V : IComponent { public float X, Y; }
    private struct M : IComponent { public float Value; }
    private readonly BW.Entity[] entities;

    public StaticWorkload(int count)
    {
        entities = new BW.Entity[count];
        BW.Create(WorldConfig.Default());
        BW.Types().Component<P>();
        BW.Types().Component<V>();
        BW.Types().Component<M>();
        BW.Initialize();
    }
    public void Create()
    {
        for (int i = 0; i < entities.Length; i++)
            entities[i] = BW.NewEntity<Default>().Set(new P { X = i, Y = i }, new V { X = 1, Y = -1 });
    }
    public void Iterate() => BW.Query().For<P, V>(static (ref P p, ref V v) => { p.X += v.X; p.Y += v.Y; });
    public void Churn()
    {
        foreach (var e in entities) e.Set(new M { Value = 10 });
        foreach (var e in entities) e.Delete<M>();
    }
    public void Destroy() { foreach (var e in entities) e.Destroy(); }
    public void Validate(int iterations, bool destroyed = false)
    {
        if (BW.Query<All<P, V>>().EntitiesCount() != (destroyed ? 0 : entities.Length))
            throw new InvalidOperationException("StaticEcs query count mismatch");
        for (int i = 0; i < entities.Length; i++)
        {
            var e = entities[i];
            if (e.IsNotDestroyed == destroyed) throw new InvalidOperationException("StaticEcs lifetime mismatch");
            if (destroyed) continue;
            ref var p = ref e.Ref<P>();
            ref var v = ref e.Ref<V>();
            if (p.X != i + iterations || p.Y != i - iterations || v.X != 1 || v.Y != -1 || e.Has<M>())
                throw new InvalidOperationException("StaticEcs component mismatch");
        }
    }
    public void Dispose() => BW.Destroy();
}

sealed class StaticStressBackend : IStressBackend
{
    private struct P : IComponent { public StressPosition Value; }
    private struct V : IComponent { public StressVelocity Value; }
    private struct Hot : IComponent { public int Value; }
    private struct T1 : IComponent { public int Value; }
    private struct T2 : IComponent { public int Value; }
    private struct T3 : IComponent { public int Value; }
    private struct T4 : IComponent { public int Value; }
    private struct T5 : IComponent { public int Value; }
    private struct T6 : IComponent { public int Value; }
    private struct T7 : IComponent { public int Value; }
    private struct T8 : IComponent { public int Value; }
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Payload : IComponent { [FieldOffset(0)] public StressPayload Value; }
    private readonly SW.Entity[] entities;
    private readonly EntityGID[] retired;
    private readonly bool[] hasRetired;

    public StaticStressBackend(int count)
    {
        entities = new SW.Entity[count];
        retired = new EntityGID[count];
        hasRetired = new bool[count];
        SW.Create(WorldConfig.Default());
        SW.Types().Component<P>();
        SW.Types().Component<V>();
        SW.Types().Component<Hot>();
        SW.Types().Component<Payload>();
        SW.Types().Component<T1>();
        SW.Types().Component<T2>();
        SW.Types().Component<T3>();
        SW.Types().Component<T4>();
        SW.Types().Component<T5>();
        SW.Types().Component<T6>();
        SW.Types().Component<T7>();
        SW.Types().Component<T8>();
        SW.Initialize();
        for (int i = 0; i < count; i++) entities[i] = Seed(i);
    }
    private static SW.Entity Seed(int i)
    {
        var e = SW.NewEntity<Default>().Set(new P { Value = StressData.Position(i) }, new V { Value = StressData.Velocity });
        int shape = StressData.Shape(i);
        if ((shape & 1) != 0) e.Set(new T1 { Value = 1 });
        if ((shape & 2) != 0) e.Set(new T2 { Value = 1 });
        if ((shape & 4) != 0) e.Set(new T3 { Value = 1 });
        if ((shape & 8) != 0) e.Set(new T4 { Value = 1 });
        if ((shape & 16) != 0) e.Set(new T5 { Value = 1 });
        if ((shape & 32) != 0) e.Set(new T6 { Value = 1 });
        if ((shape & 64) != 0) e.Set(new T7 { Value = 1 });
        if ((shape & 128) != 0) e.Set(new T8 { Value = 1 });
        if (i % StressData.HotStride == 0) e.Set(new Hot { Value = 1 });
        return e;
    }
    public void MoveAll(bool hotOnly, bool wide)
    {
        if (wide)
        {
            if (hotOnly)
                SW.Query().For<P, V, Hot, Payload>(static (ref P p, ref V v, ref Hot h, ref Payload b) =>
                {
                    p.Value.X += v.Value.X; p.Value.Y += v.Value.Y; p.Value.Z += v.Value.Z;
                    b.Value.Head++; b.Value.Tail++;
                });
            else
                SW.Query().For<P, V, Payload>(static (ref P p, ref V v, ref Payload b) =>
                {
                    p.Value.X += v.Value.X; p.Value.Y += v.Value.Y; p.Value.Z += v.Value.Z;
                    b.Value.Head++; b.Value.Tail++;
                });
        }
        else if (hotOnly)
            SW.Query().For<P, V, Hot>(static (ref P p, ref V v, ref Hot h) =>
            { p.Value.X += v.Value.X; p.Value.Y += v.Value.Y; p.Value.Z += v.Value.Z; });
        else
            SW.Query().For<P, V>(static (ref P p, ref V v) =>
            { p.Value.X += v.Value.X; p.Value.Y += v.Value.Y; p.Value.Z += v.Value.Z; });
    }
    public long ReadRandom(int[] indices)
    {
        long sum = 0;
        foreach (int i in indices)
        {
            ref var p = ref entities[i].Ref<P>().Value;
            sum += (long)p.X + p.Y + p.Z;
        }
        return sum;
    }
    public void AddPayload(int[] indices)
    {
        foreach (int i in indices) entities[i].Set(new Payload { Value = StressData.Payload(i) });
    }
    public void RemovePayload(int[] indices)
    {
        foreach (int i in indices) entities[i].Delete<Payload>();
    }
    public void Recycle(int[] indices)
    {
        foreach (int i in indices)
        {
            // Entity is a reusable slot ID; Gid captures its current generation.
            retired[i] = entities[i].GID;
            hasRetired[i] = true;
            entities[i].Destroy();
            entities[i] = Seed(i);
        }
    }
    public StressSnapshot Read(int logicalIndex)
    {
        var e = entities[logicalIndex];
        if (!e.IsNotDestroyed) return new StressSnapshot(0, 0, 0, false, false, 0, false, 0, 0);
        var p = e.Ref<P>().Value;
        int shape = 0;
        if (e.Has<T1>()) shape |= 1;
        if (e.Has<T2>()) shape |= 2;
        if (e.Has<T3>()) shape |= 4;
        if (e.Has<T4>()) shape |= 8;
        if (e.Has<T5>()) shape |= 16;
        if (e.Has<T6>()) shape |= 32;
        if (e.Has<T7>()) shape |= 64;
        if (e.Has<T8>()) shape |= 128;
        bool payload = e.Has<Payload>();
        var b = payload ? e.Ref<Payload>().Value : default;
        return new StressSnapshot(p.X, p.Y, p.Z, true, e.Has<Hot>(), shape, payload, b.Head, b.Tail);
    }
    public int Count(bool hotOnly) => hotOnly
        ? SW.Query<All<P, V, Hot>>().EntitiesCount()
        : SW.Query<All<P, V>>().EntitiesCount();
    public void ValidateRetired()
    {
        for (int i = 0; i < retired.Length; i++)
            if (hasRetired[i] && retired[i].Status<StaticStressWorld>() == GIDStatus.Active)
                throw new InvalidOperationException("StaticEcs retired handle became alive");
    }
    public void Dispose() => SW.Destroy();
}
