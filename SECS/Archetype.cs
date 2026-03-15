using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SECS;

internal sealed unsafe class Archetype : IDisposable
{
    private readonly World _world;
    private readonly Dictionary<int, int> _typeIndexLookup;
    private readonly int[] _offsets;
    private readonly int _stride;
    private readonly Dictionary<int, ComponentSetKey> _addEdges = new();
    private readonly Dictionary<int, ComponentSetKey> _removeEdges = new();
    private readonly List<Chunk> _chunks = new();

    public Archetype(World world, ComponentSetKey key, ComponentType[] componentTypes)
    {
        _world = world;
        Key = key;
        ComponentTypes = componentTypes;
        _typeIndexLookup = new Dictionary<int, int>(componentTypes.Length);
        _offsets = new int[componentTypes.Length];

        var offset = 0;
        for (var i = 0; i < componentTypes.Length; i++)
        {
            _typeIndexLookup.Add(componentTypes[i].Id, i);
            offset = Align(offset, componentTypes[i].Alignment);
            _offsets[i] = offset;
            offset += componentTypes[i].Size * world.ChunkCapacity;
        }

        _stride = Math.Max(1, Align(offset, 16));
    }

    public ComponentSetKey Key { get; }

    public ComponentType[] ComponentTypes { get; }

    public List<Chunk> Chunks => _chunks;

    public bool HasComponent(int componentTypeId) => _typeIndexLookup.ContainsKey(componentTypeId);

    public ComponentSetKey GetAddKey(int componentTypeId)
    {
        if (!_addEdges.TryGetValue(componentTypeId, out var key))
        {
            key = Key.Add(componentTypeId);
            _addEdges.Add(componentTypeId, key);
        }

        return key;
    }

    public ComponentSetKey GetRemoveKey(int componentTypeId)
    {
        if (!_removeEdges.TryGetValue(componentTypeId, out var key))
        {
            key = Key.Remove(componentTypeId);
            _removeEdges.Add(componentTypeId, key);
        }

        return key;
    }

    public World.EntitySlot AddEntity(Entity entity)
    {
        var chunkIndex = _chunks.Count - 1;
        if (chunkIndex < 0 || _chunks[chunkIndex].Count == _world.ChunkCapacity)
        {
            _chunks.Add(new Chunk(_stride, _world.ChunkCapacity, _offsets, ComponentTypes));
            chunkIndex = _chunks.Count - 1;
        }

        var chunk = _chunks[chunkIndex];
        var rowIndex = chunk.Count++;
        chunk.Entities[rowIndex] = entity.Id;
        chunk.Versions[rowIndex] = entity.Version;
        return new World.EntitySlot(chunkIndex, rowIndex);
    }

    public void CopyToWith<T>(int sourceChunkIndex, int sourceRowIndex, Archetype destination, World.EntitySlot destinationSlot, int newComponentTypeId, in T component)
        where T : unmanaged
    {
        CopySharedColumns(sourceChunkIndex, sourceRowIndex, destination, destinationSlot);
        destination.WriteComponent(destinationSlot.ChunkIndex, destinationSlot.RowIndex, newComponentTypeId, component);
    }

    public void CopyToWithout(int sourceChunkIndex, int sourceRowIndex, Archetype destination, World.EntitySlot destinationSlot, int removedComponentTypeId)
    {
        _ = removedComponentTypeId;
        CopySharedColumns(sourceChunkIndex, sourceRowIndex, destination, destinationSlot);
    }

    public RemovalResult RemoveAt(int chunkIndex, int rowIndex)
    {
        var chunk = _chunks[chunkIndex];
        var lastRow = chunk.Count - 1;

        if (rowIndex != lastRow)
        {
            chunk.Entities[rowIndex] = chunk.Entities[lastRow];
            chunk.Versions[rowIndex] = chunk.Versions[lastRow];

            for (var i = 0; i < ComponentTypes.Length; i++)
            {
                var size = ComponentTypes[i].Size;
                Buffer.MemoryCopy(
                    chunk.Memory + _offsets[i] + (lastRow * size),
                    chunk.Memory + _offsets[i] + (rowIndex * size),
                    size,
                    size);
            }
        }

        var movedEntityId = rowIndex == lastRow ? -1 : chunk.Entities[rowIndex];
        chunk.Count--;

        return rowIndex == lastRow
            ? default
            : new RemovalResult(this, movedEntityId, chunkIndex, rowIndex);
    }

    public ref T GetComponentRef<T>(int chunkIndex, int rowIndex, int componentTypeId)
        where T : unmanaged
    {
        var pointer = (T*)GetComponentPointer(chunkIndex, componentTypeId);
        return ref pointer[rowIndex];
    }

    public T ReadComponent<T>(int chunkIndex, int rowIndex, int componentTypeId)
        where T : unmanaged
    {
        return ((T*)GetComponentPointer(chunkIndex, componentTypeId))[rowIndex];
    }

    public void WriteComponent<T>(int chunkIndex, int rowIndex, int componentTypeId, in T component)
        where T : unmanaged
    {
        ((T*)GetComponentPointer(chunkIndex, componentTypeId))[rowIndex] = component;
    }

    public void Dispose()
    {
        for (var i = 0; i < _chunks.Count; i++)
        {
            _chunks[i].Dispose();
        }
    }

    private void CopySharedColumns(int sourceChunkIndex, int sourceRowIndex, Archetype destination, World.EntitySlot destinationSlot)
    {
        var source = _chunks[sourceChunkIndex];
        var target = destination._chunks[destinationSlot.ChunkIndex];

        for (var i = 0; i < ComponentTypes.Length; i++)
        {
            var componentType = ComponentTypes[i];
            if (!destination._typeIndexLookup.TryGetValue(componentType.Id, out var destinationIndex))
            {
                continue;
            }

            var size = componentType.Size;
            Buffer.MemoryCopy(
                source.Memory + _offsets[i] + (sourceRowIndex * size),
                target.Memory + destination._offsets[destinationIndex] + (destinationSlot.RowIndex * size),
                size,
                size);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void* GetComponentPointer(int chunkIndex, int componentTypeId)
    {
        return _chunks[chunkIndex].Memory + _offsets[_typeIndexLookup[componentTypeId]];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align(int value, int alignment)
    {
        var mask = alignment - 1;
        return (value + mask) & ~mask;
    }

    internal readonly struct RemovalResult
    {
        public RemovalResult(Archetype source, int movedEntityId, int movedChunkIndex, int movedRowIndex)
        {
            Source = source;
            MovedEntityId = movedEntityId;
            MovedChunkIndex = movedChunkIndex;
            MovedRowIndex = movedRowIndex;
        }

        public Archetype? Source { get; }

        public int MovedEntityId { get; }

        public int MovedChunkIndex { get; }

        public int MovedRowIndex { get; }
    }

    internal sealed unsafe class Chunk : IDisposable
    {
        private GCHandle _entitiesHandle;
        private GCHandle _versionsHandle;

        public Chunk(int stride, int capacity, int[] offsets, ComponentType[] components)
        {
            Memory = (byte*)Marshal.AllocHGlobal(stride);
            Capacity = capacity;
            Offsets = offsets;
            Components = components;
            Entities = new int[capacity];
            Versions = new int[capacity];
            _entitiesHandle = GCHandle.Alloc(Entities, GCHandleType.Pinned);
            _versionsHandle = GCHandle.Alloc(Versions, GCHandleType.Pinned);
        }

        public byte* Memory { get; }

        public int Count { get; set; }

        public int Capacity { get; }

        public int[] Entities { get; }

        public int[] Versions { get; }

        public int[] Offsets { get; }

        public ComponentType[] Components { get; }

        public void* GetComponentPointer(int componentTypeId)
        {
            for (var i = 0; i < Components.Length; i++)
            {
                if (Components[i].Id == componentTypeId)
                {
                    return Memory + Offsets[i];
                }
            }

            throw new InvalidOperationException($"Component {componentTypeId} is not in chunk.");
        }

        public RawChunkView AsRawView()
        {
            return new RawChunkView(Count, (int*)_entitiesHandle.AddrOfPinnedObject(), (int*)_versionsHandle.AddrOfPinnedObject());
        }

        public QueryChunkView<T1> AsView<T1>(int component1Id)
            where T1 : unmanaged
        {
            return new QueryChunkView<T1>(
                Count,
                (int*)_entitiesHandle.AddrOfPinnedObject(),
                (int*)_versionsHandle.AddrOfPinnedObject(),
                (T1*)GetComponentPointer(component1Id));
        }

        public QueryChunkView<T1, T2> AsView<T1, T2>(int component1Id, int component2Id)
            where T1 : unmanaged
            where T2 : unmanaged
        {
            return new QueryChunkView<T1, T2>(
                Count,
                (int*)_entitiesHandle.AddrOfPinnedObject(),
                (int*)_versionsHandle.AddrOfPinnedObject(),
                (T1*)GetComponentPointer(component1Id),
                (T2*)GetComponentPointer(component2Id));
        }

        public QueryChunkView<T1, T2, T3> AsView<T1, T2, T3>(int component1Id, int component2Id, int component3Id)
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
        {
            return new QueryChunkView<T1, T2, T3>(
                Count,
                (int*)_entitiesHandle.AddrOfPinnedObject(),
                (int*)_versionsHandle.AddrOfPinnedObject(),
                (T1*)GetComponentPointer(component1Id),
                (T2*)GetComponentPointer(component2Id),
                (T3*)GetComponentPointer(component3Id));
        }

        public void Dispose()
        {
            if (_entitiesHandle.IsAllocated)
            {
                _entitiesHandle.Free();
            }

            if (_versionsHandle.IsAllocated)
            {
                _versionsHandle.Free();
            }

            Marshal.FreeHGlobal((IntPtr)Memory);
        }
    }
}
