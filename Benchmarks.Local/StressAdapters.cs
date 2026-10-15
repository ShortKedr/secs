using System;
using System.Runtime.InteropServices;
using SKECS.Benchmarks;
using Scellecs.Morpeh;
using Leopotam.Ecs;
using Lite = Leopotam.EcsLite;
using Classic = Leopotam.Ecs;

[StructLayout(LayoutKind.Sequential)]
struct WrappedPosition : IComponent { public StressPosition Value; }
[StructLayout(LayoutKind.Sequential)]
struct WrappedVelocity : IComponent { public StressVelocity Value; }
[StructLayout(LayoutKind.Sequential)]
struct WrappedHot : IComponent { public StressHot Value; }
[StructLayout(LayoutKind.Explicit, Size = 256)]
struct WrappedPayload : IComponent { [FieldOffset(0)] public StressPayload Value; }
struct WrappedTag1 : IComponent { public StressTag1 Value; }
struct WrappedTag2 : IComponent { public StressTag2 Value; }
struct WrappedTag3 : IComponent { public StressTag3 Value; }
struct WrappedTag4 : IComponent { public StressTag4 Value; }
struct WrappedTag5 : IComponent { public StressTag5 Value; }
struct WrappedTag6 : IComponent { public StressTag6 Value; }
struct WrappedTag7 : IComponent { public StressTag7 Value; }
struct WrappedTag8 : IComponent { public StressTag8 Value; }

public sealed class LiteStressBackend : IStressBackend
{
    private readonly Lite.EcsWorld world = new();
    private readonly int[] entities;
    private readonly Lite.EcsPackedEntity[] retired;
    private int retiredCount;
    private readonly Lite.EcsPool<WrappedPosition> positions;
    private readonly Lite.EcsPool<WrappedVelocity> velocities;
    private readonly Lite.EcsPool<WrappedHot> hots;
    private readonly Lite.EcsPool<WrappedPayload> payloads;
    private readonly Lite.EcsPool<WrappedTag1> tag1;
    private readonly Lite.EcsPool<WrappedTag2> tag2;
    private readonly Lite.EcsPool<WrappedTag3> tag3;
    private readonly Lite.EcsPool<WrappedTag4> tag4;
    private readonly Lite.EcsPool<WrappedTag5> tag5;
    private readonly Lite.EcsPool<WrappedTag6> tag6;
    private readonly Lite.EcsPool<WrappedTag7> tag7;
    private readonly Lite.EcsPool<WrappedTag8> tag8;
    private readonly Lite.EcsFilter allFilter;
    private readonly Lite.EcsFilter hotFilter;
    private readonly Lite.EcsFilter wideFilter;

    public LiteStressBackend(int count)
    {
        entities = new int[count];
        retired = new Lite.EcsPackedEntity[count];
        positions = world.GetPool<WrappedPosition>();
        velocities = world.GetPool<WrappedVelocity>();
        hots = world.GetPool<WrappedHot>();
        payloads = world.GetPool<WrappedPayload>();
        tag1 = world.GetPool<WrappedTag1>();
        tag2 = world.GetPool<WrappedTag2>();
        tag3 = world.GetPool<WrappedTag3>();
        tag4 = world.GetPool<WrappedTag4>();
        tag5 = world.GetPool<WrappedTag5>();
        tag6 = world.GetPool<WrappedTag6>();
        tag7 = world.GetPool<WrappedTag7>();
        tag8 = world.GetPool<WrappedTag8>();
        allFilter = world.Filter<WrappedPosition>().Inc<WrappedVelocity>().End();
        hotFilter = world.Filter<WrappedPosition>().Inc<WrappedVelocity>().Inc<WrappedHot>().End();
        wideFilter = world.Filter<WrappedPosition>().Inc<WrappedVelocity>().Inc<WrappedPayload>().End();
        for (var i = 0; i < count; i++) entities[i] = Seed(i);
    }

    private int Seed(int index)
    {
        var entity = world.NewEntity();
        positions.Add(entity).Value = StressData.Position(index);
        velocities.Add(entity).Value = StressData.Velocity;
        var shape = StressData.Shape(index);
        if ((shape & 1) != 0) tag1.Add(entity).Value = new StressTag1 { Value = 1 };
        if ((shape & 2) != 0) tag2.Add(entity).Value = new StressTag2 { Value = 1 };
        if ((shape & 4) != 0) tag3.Add(entity).Value = new StressTag3 { Value = 1 };
        if ((shape & 8) != 0) tag4.Add(entity).Value = new StressTag4 { Value = 1 };
        if ((shape & 16) != 0) tag5.Add(entity).Value = new StressTag5 { Value = 1 };
        if ((shape & 32) != 0) tag6.Add(entity).Value = new StressTag6 { Value = 1 };
        if ((shape & 64) != 0) tag7.Add(entity).Value = new StressTag7 { Value = 1 };
        if ((shape & 128) != 0) tag8.Add(entity).Value = new StressTag8 { Value = 1 };
        if (index % StressData.HotStride == 0) hots.Add(entity).Value = new StressHot { Value = 1 };
        return entity;
    }

    public void MoveAll(bool hotOnly, bool wide)
    {
        var filter = wide ? wideFilter : (hotOnly ? hotFilter : allFilter);
        if (wide)
        {
            foreach (var entity in filter)
            {
                ref var p = ref positions.Get(entity).Value;
                ref var v = ref velocities.Get(entity).Value;
                p.X += v.X; p.Y += v.Y; p.Z += v.Z;
                ref var payload = ref payloads.Get(entity).Value;
                payload.Head += 1; payload.Tail += 1;
            }
        }
        else
        {
            foreach (var entity in filter)
            {
                ref var p = ref positions.Get(entity).Value;
                ref var v = ref velocities.Get(entity).Value;
                p.X += v.X; p.Y += v.Y; p.Z += v.Z;
            }
        }
    }

    public long ReadRandom(int[] indices)
    {
        long sum = 0;
        foreach (var index in indices)
        {
            ref var p = ref positions.Get(entities[index]).Value;
            sum += (long)p.X + p.Y + p.Z;
        }
        return sum;
    }

    public void AddPayload(int[] indices)
    {
        foreach (var index in indices) payloads.Add(entities[index]).Value = StressData.Payload(index);
    }

    public void RemovePayload(int[] indices)
    {
        foreach (var index in indices) payloads.Del(entities[index]);
    }

    public void Recycle(int[] indices)
    {
        retiredCount = 0;
        foreach (var index in indices)
        {
            var entity = entities[index];
            retired[retiredCount++] = Lite.EcsEntityExtensions.PackEntity(world, entity);
            world.DelEntity(entity);
            entities[index] = Seed(index);
        }
    }

    public StressSnapshot Read(int logicalIndex)
    {
        var entity = entities[logicalIndex];
        var alive = world.GetEntityGen(entity) > 0;
        if (!alive) return new StressSnapshot(0, 0, 0, false, false, 0, false, 0, 0);
        var p = positions.Get(entity).Value;
        var shape = 0;
        if (tag1.Has(entity)) shape |= 1;
        if (tag2.Has(entity)) shape |= 2;
        if (tag3.Has(entity)) shape |= 4;
        if (tag4.Has(entity)) shape |= 8;
        if (tag5.Has(entity)) shape |= 16;
        if (tag6.Has(entity)) shape |= 32;
        if (tag7.Has(entity)) shape |= 64;
        if (tag8.Has(entity)) shape |= 128;
        var hasPayload = payloads.Has(entity);
        var payload = hasPayload ? payloads.Get(entity).Value : default;
        return new StressSnapshot(p.X, p.Y, p.Z, true, hots.Has(entity), shape,
            hasPayload, payload.Head, payload.Tail);
    }

    public int Count(bool hotOnly) => hotOnly ? hotFilter.GetEntitiesCount() : allFilter.GetEntitiesCount();

    public void ValidateRetired()
    {
        // Retired handles retain their generation even when a slot is reused.
        for (var i = 0; i < retiredCount; i++)
            if (Lite.EcsEntityExtensions.Unpack(in retired[i], world, out _))
                throw new InvalidOperationException("Lite: retired entity is still alive.");
    }

    public void Dispose() => world.Destroy();
}

public sealed class ClassicStressBackend : IStressBackend
{
    private readonly Classic.EcsWorld world = new();
    private readonly Classic.EcsEntity[] entities;
    private readonly Classic.EcsEntity[] retired;
    private int retiredCount;
    private readonly Classic.EcsFilter<WrappedPosition, WrappedVelocity> allFilter;
    private readonly Classic.EcsFilter<WrappedPosition, WrappedVelocity, WrappedHot> hotFilter;
    private readonly Classic.EcsFilter<WrappedPosition, WrappedVelocity, WrappedPayload> wideFilter;

    public ClassicStressBackend(int count)
    {
        entities = new Classic.EcsEntity[count];
        retired = new Classic.EcsEntity[count];
        allFilter = (Classic.EcsFilter<WrappedPosition, WrappedVelocity>)world.GetFilter(typeof(Classic.EcsFilter<WrappedPosition, WrappedVelocity>));
        hotFilter = (Classic.EcsFilter<WrappedPosition, WrappedVelocity, WrappedHot>)world.GetFilter(typeof(Classic.EcsFilter<WrappedPosition, WrappedVelocity, WrappedHot>));
        wideFilter = (Classic.EcsFilter<WrappedPosition, WrappedVelocity, WrappedPayload>)world.GetFilter(typeof(Classic.EcsFilter<WrappedPosition, WrappedVelocity, WrappedPayload>));
        for (var i = 0; i < count; i++) entities[i] = Seed(i);
    }

    private Classic.EcsEntity Seed(int index)
    {
        var entity = world.NewEntity();
        entity.Get<WrappedPosition>().Value = StressData.Position(index);
        entity.Get<WrappedVelocity>().Value = StressData.Velocity;
        var shape = StressData.Shape(index);
        if ((shape & 1) != 0) entity.Get<WrappedTag1>().Value = new StressTag1 { Value = 1 };
        if ((shape & 2) != 0) entity.Get<WrappedTag2>().Value = new StressTag2 { Value = 1 };
        if ((shape & 4) != 0) entity.Get<WrappedTag3>().Value = new StressTag3 { Value = 1 };
        if ((shape & 8) != 0) entity.Get<WrappedTag4>().Value = new StressTag4 { Value = 1 };
        if ((shape & 16) != 0) entity.Get<WrappedTag5>().Value = new StressTag5 { Value = 1 };
        if ((shape & 32) != 0) entity.Get<WrappedTag6>().Value = new StressTag6 { Value = 1 };
        if ((shape & 64) != 0) entity.Get<WrappedTag7>().Value = new StressTag7 { Value = 1 };
        if ((shape & 128) != 0) entity.Get<WrappedTag8>().Value = new StressTag8 { Value = 1 };
        if (index % StressData.HotStride == 0) entity.Get<WrappedHot>().Value = new StressHot { Value = 1 };
        return entity;
    }

    public void MoveAll(bool hotOnly, bool wide)
    {
        if (!hotOnly && !wide)
        {
            for (var i = 0; i < allFilter.GetEntitiesCount(); i++)
            {
                ref var p = ref allFilter.Get1(i).Value;
                ref var v = ref allFilter.Get2(i).Value;
                p.X += v.X; p.Y += v.Y; p.Z += v.Z;
            }
            return;
        }
        if (hotOnly && !wide)
        {
            for (var i = 0; i < hotFilter.GetEntitiesCount(); i++)
            {
                ref var p = ref hotFilter.Get1(i).Value;
                ref var v = ref hotFilter.Get2(i).Value;
                p.X += v.X; p.Y += v.Y; p.Z += v.Z;
            }
            return;
        }
        if (wide)
        {
            for (var i = 0; i < wideFilter.GetEntitiesCount(); i++)
            {
                ref var p = ref wideFilter.Get1(i).Value;
                ref var v = ref wideFilter.Get2(i).Value;
                p.X += v.X; p.Y += v.Y; p.Z += v.Z;
                ref var payload = ref wideFilter.Get3(i).Value;
                payload.Head += 1; payload.Tail += 1;
            }
            return;
        }
    }

    public long ReadRandom(int[] indices)
    {
        long sum = 0;
        foreach (var index in indices)
        {
            ref var p = ref entities[index].Get<WrappedPosition>().Value;
            sum += (long)p.X + p.Y + p.Z;
        }
        return sum;
    }

    public void AddPayload(int[] indices)
    {
        foreach (var index in indices) entities[index].Get<WrappedPayload>().Value = StressData.Payload(index);
    }

    public void RemovePayload(int[] indices)
    {
        foreach (var index in indices) entities[index].Del<WrappedPayload>();
    }

    public void Recycle(int[] indices)
    {
        retiredCount = 0;
        foreach (var index in indices)
        {
            var entity = entities[index];
            retired[retiredCount++] = entity;
            entity.Destroy();
            entities[index] = Seed(index);
        }
    }

    public StressSnapshot Read(int logicalIndex)
    {
        var entity = entities[logicalIndex];
        var alive = entity.IsAlive();
        if (!alive) return new StressSnapshot(0, 0, 0, false, false, 0, false, 0, 0);
        var p = entity.Get<WrappedPosition>().Value;
        var shape = 0;
        if (entity.Has<WrappedTag1>()) shape |= 1;
        if (entity.Has<WrappedTag2>()) shape |= 2;
        if (entity.Has<WrappedTag3>()) shape |= 4;
        if (entity.Has<WrappedTag4>()) shape |= 8;
        if (entity.Has<WrappedTag5>()) shape |= 16;
        if (entity.Has<WrappedTag6>()) shape |= 32;
        if (entity.Has<WrappedTag7>()) shape |= 64;
        if (entity.Has<WrappedTag8>()) shape |= 128;
        var hasPayload = entity.Has<WrappedPayload>();
        var payload = hasPayload ? entity.Get<WrappedPayload>().Value : default;
        return new StressSnapshot(p.X, p.Y, p.Z, true, entity.Has<WrappedHot>(), shape,
            hasPayload, payload.Head, payload.Tail);
    }

    public int Count(bool hotOnly) => hotOnly ? hotFilter.GetEntitiesCount() : allFilter.GetEntitiesCount();

    public void ValidateRetired()
    {
        // Retired handles retain their generation even when a slot is reused.
        for (var i = 0; i < retiredCount; i++)
            if (retired[i].IsAlive())
                throw new InvalidOperationException("Classic: retired entity is still alive.");
    }

    public void Dispose() => world.Destroy();
}

public sealed class MorpehStressBackend : IStressBackend
{
    private readonly World world = World.Create();
    private readonly Entity[] entities;
    private readonly Entity[] retired;
    private int retiredCount;
    private readonly Stash<WrappedPosition> positions;
    private readonly Stash<WrappedVelocity> velocities;
    private readonly Stash<WrappedHot> hots;
    private readonly Stash<WrappedPayload> payloads;
    private readonly Stash<WrappedTag1> tag1;
    private readonly Stash<WrappedTag2> tag2;
    private readonly Stash<WrappedTag3> tag3;
    private readonly Stash<WrappedTag4> tag4;
    private readonly Stash<WrappedTag5> tag5;
    private readonly Stash<WrappedTag6> tag6;
    private readonly Stash<WrappedTag7> tag7;
    private readonly Stash<WrappedTag8> tag8;
    private readonly Filter allFilter;
    private readonly Filter hotFilter;
    private readonly Filter wideFilter;

    public MorpehStressBackend(int count)
    {
        entities = new Entity[count];
        retired = new Entity[count];
        positions = world.GetStash<WrappedPosition>();
        velocities = world.GetStash<WrappedVelocity>();
        hots = world.GetStash<WrappedHot>();
        payloads = world.GetStash<WrappedPayload>();
        tag1 = world.GetStash<WrappedTag1>();
        tag2 = world.GetStash<WrappedTag2>();
        tag3 = world.GetStash<WrappedTag3>();
        tag4 = world.GetStash<WrappedTag4>();
        tag5 = world.GetStash<WrappedTag5>();
        tag6 = world.GetStash<WrappedTag6>();
        tag7 = world.GetStash<WrappedTag7>();
        tag8 = world.GetStash<WrappedTag8>();
        allFilter = world.Filter.With<WrappedPosition>().With<WrappedVelocity>().Build();
        hotFilter = world.Filter.With<WrappedPosition>().With<WrappedVelocity>().With<WrappedHot>().Build();
        wideFilter = world.Filter.With<WrappedPosition>().With<WrappedVelocity>().With<WrappedPayload>().Build();
        for (var i = 0; i < count; i++) entities[i] = Seed(i);
        world.Commit();
    }

    private Entity Seed(int index)
    {
        var entity = world.CreateEntity();
        positions.Add(entity).Value = StressData.Position(index);
        velocities.Add(entity).Value = StressData.Velocity;
        var shape = StressData.Shape(index);
        if ((shape & 1) != 0) tag1.Add(entity).Value = new StressTag1 { Value = 1 };
        if ((shape & 2) != 0) tag2.Add(entity).Value = new StressTag2 { Value = 1 };
        if ((shape & 4) != 0) tag3.Add(entity).Value = new StressTag3 { Value = 1 };
        if ((shape & 8) != 0) tag4.Add(entity).Value = new StressTag4 { Value = 1 };
        if ((shape & 16) != 0) tag5.Add(entity).Value = new StressTag5 { Value = 1 };
        if ((shape & 32) != 0) tag6.Add(entity).Value = new StressTag6 { Value = 1 };
        if ((shape & 64) != 0) tag7.Add(entity).Value = new StressTag7 { Value = 1 };
        if ((shape & 128) != 0) tag8.Add(entity).Value = new StressTag8 { Value = 1 };
        if (index % StressData.HotStride == 0) hots.Add(entity).Value = new StressHot { Value = 1 };
        return entity;
    }

    public void MoveAll(bool hotOnly, bool wide)
    {
        var filter = wide ? wideFilter : (hotOnly ? hotFilter : allFilter);
        if (wide)
        {
            foreach (var entity in filter)
            {
                ref var p = ref positions.Get(entity).Value;
                ref var v = ref velocities.Get(entity).Value;
                p.X += v.X; p.Y += v.Y; p.Z += v.Z;
                ref var payload = ref payloads.Get(entity).Value;
                payload.Head += 1; payload.Tail += 1;
            }
        }
        else
        {
            foreach (var entity in filter)
            {
                ref var p = ref positions.Get(entity).Value;
                ref var v = ref velocities.Get(entity).Value;
                p.X += v.X; p.Y += v.Y; p.Z += v.Z;
            }
        }
    }

    public long ReadRandom(int[] indices)
    {
        long sum = 0;
        foreach (var index in indices)
        {
            ref var p = ref positions.Get(entities[index]).Value;
            sum += (long)p.X + p.Y + p.Z;
        }
        return sum;
    }

    public void AddPayload(int[] indices)
    {
        foreach (var index in indices) payloads.Add(entities[index]).Value = StressData.Payload(index);
        world.Commit();
    }

    public void RemovePayload(int[] indices)
    {
        foreach (var index in indices) payloads.Remove(entities[index]);
        world.Commit();
    }

    public void Recycle(int[] indices)
    {
        retiredCount = 0;
        foreach (var index in indices)
        {
            var entity = entities[index];
            retired[retiredCount++] = entity;
            world.RemoveEntity(entity);
            entities[index] = Seed(index);
        }
        world.Commit();
    }

    public StressSnapshot Read(int logicalIndex)
    {
        var entity = entities[logicalIndex];
        var alive = !world.IsDisposed(entity);
        if (!alive) return new StressSnapshot(0, 0, 0, false, false, 0, false, 0, 0);
        var p = positions.Get(entity).Value;
        var shape = 0;
        if (tag1.Has(entity)) shape |= 1;
        if (tag2.Has(entity)) shape |= 2;
        if (tag3.Has(entity)) shape |= 4;
        if (tag4.Has(entity)) shape |= 8;
        if (tag5.Has(entity)) shape |= 16;
        if (tag6.Has(entity)) shape |= 32;
        if (tag7.Has(entity)) shape |= 64;
        if (tag8.Has(entity)) shape |= 128;
        var hasPayload = payloads.Has(entity);
        var payload = hasPayload ? payloads.Get(entity).Value : default;
        return new StressSnapshot(p.X, p.Y, p.Z, true, hots.Has(entity), shape,
            hasPayload, payload.Head, payload.Tail);
    }

    public int Count(bool hotOnly) => (hotOnly ? hotFilter : allFilter).GetLengthSlow();

    public void ValidateRetired()
    {
        // Retired handles retain their generation even when a slot is reused.
        for (var i = 0; i < retiredCount; i++)
            if (!world.IsDisposed(retired[i]))
                throw new InvalidOperationException("Morpeh: retired entity is still alive.");
    }

    public void Dispose() => world.Dispose();
}
