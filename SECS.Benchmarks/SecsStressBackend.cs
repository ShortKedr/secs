using SECS;

namespace SECS.Benchmarks;

public sealed class SecsStressBackend : IStressBackend
{
    private readonly World _world = new(new WorldOptions(chunkCapacity: 256));
    private readonly Entity[] _entities;
    private readonly Entity[] _retired;
    private readonly bool[] _hasRetired;
    private readonly Query<StressPosition, StressVelocity> _dense;
    private readonly Query<StressPosition, StressVelocity, StressHot> _hot;
    private readonly Query<StressPosition, StressVelocity, StressPayload> _wide;

    public SecsStressBackend(int count)
    {
        _entities = new Entity[count];
        _retired = new Entity[count];
        _hasRetired = new bool[count];
        _dense = _world.Query<StressPosition, StressVelocity>();
        _hot = _world.Query<StressPosition, StressVelocity, StressHot>();
        _wide = _world.Query<StressPosition, StressVelocity, StressPayload>();
        for (var i = 0; i < count; i++) Seed(i);
    }

    private void Seed(int index)
    {
        var entity = _world.CreateEntity();
        _entities[index] = entity;
        _world.Add(entity, StressData.Position(index));
        _world.Add(entity, StressData.Velocity);
        if (index % StressData.HotStride == 0) _world.Add(entity, new StressHot { Value = 1 });
        var shape = StressData.Shape(index);
        if ((shape & 1) != 0) _world.Add(entity, new StressTag1 { Value = 1 });
        if ((shape & 2) != 0) _world.Add(entity, new StressTag2 { Value = 1 });
        if ((shape & 4) != 0) _world.Add(entity, new StressTag3 { Value = 1 });
        if ((shape & 8) != 0) _world.Add(entity, new StressTag4 { Value = 1 });
        if ((shape & 16) != 0) _world.Add(entity, new StressTag5 { Value = 1 });
        if ((shape & 32) != 0) _world.Add(entity, new StressTag6 { Value = 1 });
        if ((shape & 64) != 0) _world.Add(entity, new StressTag7 { Value = 1 });
        if ((shape & 128) != 0) _world.Add(entity, new StressTag8 { Value = 1 });
    }

    public void MoveAll(bool hotOnly, bool wide)
    {
        if (wide)
        {
            var processor = new WideMove();
            _wide.ForEach(ref processor);
        }
        else if (hotOnly)
        {
            var processor = new HotMove();
            _hot.ForEach(ref processor);
        }
        else
        {
            var processor = new DenseMove();
            _dense.ForEach(ref processor);
        }
    }

    public long ReadRandom(int[] indices)
    {
        long sum = 0;
        foreach (var index in indices)
        {
            ref var position = ref _world.Get<StressPosition>(_entities[index]);
            sum += (long)position.X + position.Y + position.Z;
        }
        return sum;
    }

    public void AddPayload(int[] indices)
    {
        foreach (var index in indices) _world.Add(_entities[index], StressData.Payload(index));
    }
    public void RemovePayload(int[] indices)
    {
        foreach (var index in indices) _world.Remove<StressPayload>(_entities[index]);
    }
    public void Recycle(int[] indices)
    {
        foreach (var index in indices)
        {
            _retired[index] = _entities[index];
            _hasRetired[index] = true;
            _world.DestroyEntity(_entities[index]);
            Seed(index);
        }
    }

    public StressSnapshot Read(int index)
    {
        var entity = _entities[index];
        var alive = _world.IsAlive(entity);
        if (!alive) return new StressSnapshot(0, 0, 0, false, false, 0, false, 0, 0);
        var p = _world.Get<StressPosition>(entity);
        var shape = 0;
        if (_world.TryGet<StressTag1>(entity, out _)) shape |= 1;
        if (_world.TryGet<StressTag2>(entity, out _)) shape |= 2;
        if (_world.TryGet<StressTag3>(entity, out _)) shape |= 4;
        if (_world.TryGet<StressTag4>(entity, out _)) shape |= 8;
        if (_world.TryGet<StressTag5>(entity, out _)) shape |= 16;
        if (_world.TryGet<StressTag6>(entity, out _)) shape |= 32;
        if (_world.TryGet<StressTag7>(entity, out _)) shape |= 64;
        if (_world.TryGet<StressTag8>(entity, out _)) shape |= 128;
        var hasPayload = _world.TryGet<StressPayload>(entity, out var payload);
        return new StressSnapshot(p.X, p.Y, p.Z, alive, _world.TryGet<StressHot>(entity, out _),
            shape, hasPayload, payload.Head, payload.Tail);
    }
    public int Count(bool hotOnly)
    {
        var count = 0;
        if (hotOnly) { foreach (var chunk in _hot.Chunks()) count += chunk.Count; }
        else { foreach (var chunk in _dense.Chunks()) count += chunk.Count; }
        return count;
    }
    public void ValidateRetired()
    {
        for (var i = 0; i < _retired.Length; i++)
            if (_hasRetired[i] && _world.IsAlive(_retired[i])) throw new InvalidOperationException("Retired entity revived");
    }
    public void Dispose() => _world.Dispose();

    private static void Move(ref StressPosition p, ref StressVelocity v)
    {
        p.X += v.X; p.Y += v.Y; p.Z += v.Z;
    }
    private struct DenseMove : IQueryProcessor<StressPosition, StressVelocity>
    {
        public void Execute(Entity entity, ref StressPosition p, ref StressVelocity v) => Move(ref p, ref v);
    }
    private struct HotMove : IQueryProcessor<StressPosition, StressVelocity, StressHot>
    {
        public void Execute(Entity entity, ref StressPosition p, ref StressVelocity v, ref StressHot hot) => Move(ref p, ref v);
    }
    private struct WideMove : IQueryProcessor<StressPosition, StressVelocity, StressPayload>
    {
        public void Execute(Entity entity, ref StressPosition p, ref StressVelocity v, ref StressPayload payload)
        {
            Move(ref p, ref v); payload.Head++; payload.Tail++;
        }
    }
}
