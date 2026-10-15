using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SKECS;

internal sealed unsafe class Archetype : IDisposable
{
    private readonly World _world;
    private readonly int[] _columnByType;
    private readonly int[] _offsetByType;
    private readonly int[] _offsets;
    private readonly int _stride;
    private readonly Dictionary<int, Transition> _addEdges = new();
    private readonly Dictionary<int, Transition> _removeEdges = new();
    private readonly List<Chunk> _chunks = new();
    private readonly Stack<int> _availableChunks = new();

    public Archetype(World world, ComponentSetKey key, ComponentType[] componentTypes)
    {
        _world = world;
        Key = key;
        ComponentTypes = componentTypes;
        var maxTypeId = -1;
        foreach (var type in componentTypes) maxTypeId = Math.Max(maxTypeId, type.Id);
        _columnByType = new int[maxTypeId + 1];
        _offsetByType = new int[maxTypeId + 1];
        for (var i = 0; i < _columnByType.Length; i++)
        {
            _columnByType[i] = -1;
            _offsetByType[i] = -1;
        }
        _offsets = new int[componentTypes.Length];

        var offset = 0;
        for (var i = 0; i < componentTypes.Length; i++)
        {
            _columnByType[componentTypes[i].Id] = i;
            offset = Align(offset, componentTypes[i].Alignment);
            _offsets[i] = offset;
            _offsetByType[componentTypes[i].Id] = offset;
            offset += componentTypes[i].Size * world.ChunkCapacity;
        }

        _stride = Math.Max(1, Align(offset, 16));
    }

    public ComponentSetKey Key { get; }

    public ComponentType[] ComponentTypes { get; }

    public List<Chunk> Chunks => _chunks;

    public bool HasComponent(int componentTypeId) => (uint)componentTypeId < (uint)_columnByType.Length && _columnByType[componentTypeId] >= 0;

    internal int ComponentSize(int typeId) => ComponentTypes[_columnByType[typeId]].Size;

    public Transition GetAddTransition(int componentTypeId)
    {
        if (!_addEdges.TryGetValue(componentTypeId, out var transition))
        {
            transition = new Transition(this, _world.GetArchetype(Key.Add(componentTypeId)));
            _addEdges.Add(componentTypeId, transition);
        }

        return transition;
    }

    public Transition GetRemoveTransition(int componentTypeId)
    {
        if (!_removeEdges.TryGetValue(componentTypeId, out var transition))
        {
            transition = new Transition(this, _world.GetArchetype(Key.Remove(componentTypeId)));
            _removeEdges.Add(componentTypeId, transition);
        }

        return transition;
    }

    public World.EntitySlot AddEntity(Entity entity)
    {
        if (_availableChunks.Count == 0)
        {
            _chunks.Add(new Chunk(_stride, _world.ChunkCapacity, _offsets, ComponentTypes, _offsetByType));
            _availableChunks.Push(_chunks.Count - 1);
        }

        var chunkIndex = _availableChunks.Peek();
        var chunk = _chunks[chunkIndex];
        var rowIndex = chunk.Count++;
        if (chunk.Count == _world.ChunkCapacity) _availableChunks.Pop();
        chunk.Entities[rowIndex] = entity.Id;
        chunk.Versions[rowIndex] = entity.Version;
        return new World.EntitySlot(chunkIndex, rowIndex);
    }

    public RemovalResult RemoveAt(int chunkIndex, int rowIndex)
    {
        var chunk = _chunks[chunkIndex];
        if (chunk.Count == _world.ChunkCapacity) _availableChunks.Push(chunkIndex);
        var lastRow = chunk.Count - 1;

        if (rowIndex != lastRow)
        {
            chunk.Entities[rowIndex] = chunk.Entities[lastRow];
            chunk.Versions[rowIndex] = chunk.Versions[lastRow];

            for (var i = 0; i < ComponentTypes.Length; i++)
            {
                var size = ComponentTypes[i].Size;
                CopyComponent(chunk.Memory + _offsets[i] + lastRow * size,
                    chunk.Memory + _offsets[i] + rowIndex * size, size);
            }
        }

        var movedEntityId = rowIndex == lastRow ? -1 : chunk.Entities[rowIndex];
        chunk.Count--;

        return rowIndex == lastRow
            ? new RemovalResult(this, -1, chunkIndex, rowIndex)
            : new RemovalResult(this, movedEntityId, chunkIndex, rowIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    public long NativeBytes => (long)_stride * _chunks.Count;

    internal sealed class Transition
    {
        private readonly Archetype _source;
        private readonly CopyColumn[] _columns;
        public Archetype Destination { get; }

        public Transition(Archetype source, Archetype destination)
        {
            _source = source;
            Destination = destination;
            var columns = new List<CopyColumn>(source.ComponentTypes.Length);
            for (var i = 0; i < source.ComponentTypes.Length; i++)
            {
                var type = source.ComponentTypes[i];
                if (destination.HasComponent(type.Id))
                    columns.Add(new CopyColumn(source._offsets[i], destination._offsets[destination._columnByType[type.Id]], type.Size));
            }
            _columns = columns.ToArray();
        }

        public void Copy(int sourceChunkIndex, int sourceRowIndex, World.EntitySlot destinationSlot)
        {
            var source = _source._chunks[sourceChunkIndex].Memory;
            var target = Destination._chunks[destinationSlot.ChunkIndex].Memory;
            foreach (var column in _columns)
            {
                CopyComponent(source + column.SourceOffset + sourceRowIndex * column.Size,
                    target + column.DestinationOffset + destinationSlot.RowIndex * column.Size, column.Size);
            }
        }

        private readonly struct CopyColumn
        {
            public CopyColumn(int sourceOffset, int destinationOffset, int size)
            { SourceOffset = sourceOffset; DestinationOffset = destinationOffset; Size = size; }
            public readonly int SourceOffset;
            public readonly int DestinationOffset;
            public readonly int Size;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyComponent(byte* source, byte* destination, int size)
    {
#if NET8_0_OR_GREATER
        // Small columns are common in transitions; avoid a general-purpose memmove per field.
        switch (size)
        {
            case 4: Unsafe.WriteUnaligned(destination, Unsafe.ReadUnaligned<int>(source)); break;
            case 8: Unsafe.WriteUnaligned(destination, Unsafe.ReadUnaligned<long>(source)); break;
            case 12:
                Unsafe.WriteUnaligned(destination, Unsafe.ReadUnaligned<long>(source));
                Unsafe.WriteUnaligned(destination + 8, Unsafe.ReadUnaligned<int>(source + 8));
                break;
            default: Buffer.MemoryCopy(source, destination, size, size); break;
        }
#else
        Buffer.MemoryCopy(source, destination, size, size);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void* GetComponentPointer(int chunkIndex, int componentTypeId)
    {
        if ((uint)componentTypeId >= (uint)_offsetByType.Length || _offsetByType[componentTypeId] < 0)
            throw new InvalidOperationException($"Component {componentTypeId} is not in archetype.");
        return _chunks[chunkIndex].Memory + _offsetByType[componentTypeId];
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
        private readonly int[] _offsetByType;

        public Chunk(int stride, int capacity, int[] offsets, ComponentType[] components, int[] offsetByType)
        {
            Memory = (byte*)Marshal.AllocHGlobal(stride);
            Capacity = capacity;
            Offsets = offsets;
            Components = components;
            _offsetByType = offsetByType;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void* GetComponentPointer(int componentTypeId)
        {
            if ((uint)componentTypeId >= (uint)_offsetByType.Length || _offsetByType[componentTypeId] < 0)
                throw new InvalidOperationException($"Component {componentTypeId} is not in chunk.");
            return Memory + _offsetByType[componentTypeId];
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
