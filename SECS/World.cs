using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SECS;

public sealed class World : IDisposable
{
    private readonly WorldOptions _options;
    private readonly ComponentTypeRegistry _componentTypes = new();
    private readonly Dictionary<ComponentSetKey, Archetype> _archetypes = new();
    private readonly List<Archetype> _archetypeList = new();
    private readonly Dictionary<QueryCacheKey, QueryCacheEntry> _queryCache = new();
    private readonly List<EntityRecord> _entityRecords = new();
    private readonly Stack<int> _freeEntityIds = new();
    private readonly Dictionary<SystemStage, List<Action<World>>> _systems = new();
    private readonly Archetype _rootArchetype;
    private readonly Dictionary<(int, int, int), Archetype> _creationArchetypes = new();
    private readonly List<AccessorStorage> _accessors = new();
    private bool _disposed;

    public World(WorldOptions options = default)
    {
        _options = options.Equals(default(WorldOptions)) ? new WorldOptions() : options;
        _rootArchetype = new Archetype(this, ComponentSetKey.Empty, Array.Empty<ComponentType>());
        _archetypes.Add(ComponentSetKey.Empty, _rootArchetype);
        _archetypeList.Add(_rootArchetype);

        _systems[SystemStage.PreUpdate] = new List<Action<World>>();
        _systems[SystemStage.Update] = new List<Action<World>>();
        _systems[SystemStage.PostUpdate] = new List<Action<World>>();
    }

    public int ChunkCapacity => _options.ChunkCapacity;

    public bool DiagnosticsEnabled => _options.DiagnosticsEnabled;

    public Entity CreateEntity()
        => CreateEntityIn(_rootArchetype);

    internal Entity CreateEntityIn(Archetype archetype)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(World));
        var id = _freeEntityIds.Count > 0 ? _freeEntityIds.Pop() : _entityRecords.Count;
        var version = id < _entityRecords.Count ? _entityRecords[id].Version : 1;

        if (id == _entityRecords.Count)
        {
            _entityRecords.Add(default);
        }

        var record = _entityRecords[id];
        record.Version = version;
        record.Alive = true;

        var slot = archetype.AddEntity(new Entity(id, version));
        record.Archetype = archetype;
        record.ChunkIndex = slot.ChunkIndex;
        record.RowIndex = slot.RowIndex;
        _entityRecords[id] = record;
        RefreshAccessors(id, record);

        return new Entity(id, version);
    }

    public Entity CreateEntity<T1>(in T1 component1) where T1 : unmanaged
    {
        var type1 = _componentTypes.GetOrCreate<T1>().Id;
        var archetype = GetCreationArchetype(type1, -1, -1);
        var entity = CreateEntityIn(archetype);
        var record = _entityRecords[entity.Id];
        archetype.WriteComponent(record.ChunkIndex, record.RowIndex, type1, component1);
        return entity;
    }

    public Entity CreateEntity<T1, T2>(in T1 component1, in T2 component2)
        where T1 : unmanaged where T2 : unmanaged
        => PrepareEntity<T1, T2>().Create(component1, component2);

    public Entity CreateEntity<T1, T2, T3>(in T1 component1, in T2 component2, in T3 component3)
        where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
    {
        var type1 = _componentTypes.GetOrCreate<T1>().Id;
        var type2 = _componentTypes.GetOrCreate<T2>().Id;
        var type3 = _componentTypes.GetOrCreate<T3>().Id;
        var archetype = GetCreationArchetype(type1, type2, type3);
        var entity = CreateEntityIn(archetype);
        var record = _entityRecords[entity.Id];
        archetype.WriteComponent(record.ChunkIndex, record.RowIndex, type1, component1);
        archetype.WriteComponent(record.ChunkIndex, record.RowIndex, type2, component2);
        archetype.WriteComponent(record.ChunkIndex, record.RowIndex, type3, component3);
        return entity;
    }

    public EntityFactory<T1, T2> PrepareEntity<T1, T2>() where T1 : unmanaged where T2 : unmanaged
    {
        var type1 = _componentTypes.GetOrCreate<T1>().Id;
        var type2 = _componentTypes.GetOrCreate<T2>().Id;
        return new EntityFactory<T1, T2>(this, GetCreationArchetype(type1, type2, -1), type1, type2);
    }

    public void CreateEntities<T1, T2>(Span<Entity> output, in T1 component1, in T2 component2)
        where T1 : unmanaged where T2 : unmanaged
        => PrepareEntity<T1, T2>().CreateBatch(output, component1, component2);

    private unsafe Archetype GetCreationArchetype(int type1, int type2, int type3)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(World));
        if (type1 == type2 || type1 == type3 || (type2 >= 0 && type2 == type3))
            throw new ArgumentException("A creation template cannot contain duplicate component types.");
        var key = (type1, type2, type3);
        if (_creationArchetypes.TryGetValue(key, out var archetype)) return archetype;
        var ids = stackalloc int[3];
        ids[0] = type1;
        ids[1] = type2;
        ids[2] = type3;
        archetype = GetArchetype(ComponentSetKey.Create(ids, type3 >= 0 ? 3 : type2 >= 0 ? 2 : 1));
        _creationArchetypes.Add(key, archetype);
        return archetype;
    }

    internal Entity CreateEntityIn<T1, T2>(Archetype archetype, int type1, int type2, in T1 component1, in T2 component2)
        where T1 : unmanaged where T2 : unmanaged
    {
        var entity = CreateEntityIn(archetype);
        var record = _entityRecords[entity.Id];
        archetype.WriteComponent(record.ChunkIndex, record.RowIndex, type1, component1);
        archetype.WriteComponent(record.ChunkIndex, record.RowIndex, type2, component2);
        return entity;
    }

    public ComponentAccessor<T> GetAccessor<T>() where T : unmanaged
    {
        if (_disposed) throw new ObjectDisposedException(nameof(World));
        var typeId = _componentTypes.GetOrCreate<T>().Id;
        foreach (var accessor in _accessors)
            if (accessor.TypeId == typeId) return new ComponentAccessor<T>(accessor);
        var storage = new AccessorStorage(typeId);
        for (var id = 0; id < _entityRecords.Count; id++) storage.Update(id, _entityRecords[id]);
        _accessors.Add(storage);
        return new ComponentAccessor<T>(storage);
    }

    public StorageStatistics GetStorageStatistics()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(World));
        var chunks = 0;
        var entities = 0;
        long nativeBytes = 0;
        foreach (var archetype in _archetypeList)
        {
            chunks += archetype.Chunks.Count;
            nativeBytes += archetype.NativeBytes;
            foreach (var chunk in archetype.Chunks) entities += chunk.Count;
        }
        long accessorBytes = 0;
        foreach (var accessor in _accessors) accessorBytes += accessor.ManagedBufferBytes;
        return new StorageStatistics(_archetypeList.Count, chunks, entities, nativeBytes, accessorBytes);
    }

    public bool IsAlive(Entity entity)
    {
        if ((uint)entity.Id >= (uint)_entityRecords.Count)
        {
            return false;
        }

        var record = _entityRecords[entity.Id];
        return record.Alive && record.Version == entity.Version;
    }

    public void DestroyEntity(Entity entity)
    {
        ValidateEntity(entity);

        var record = _entityRecords[entity.Id];
        var removed = record.Archetype!.RemoveAt(record.ChunkIndex, record.RowIndex);
        UpdateMovedEntity(removed);

        record.Archetype = null;
        record.ChunkIndex = -1;
        record.RowIndex = -1;
        record.Alive = false;
        record.Version++;
        _entityRecords[entity.Id] = record;
        RefreshAccessors(entity.Id, record);
        _freeEntityIds.Push(entity.Id);
    }

    public void Add<T>(Entity entity, in T component)
        where T : unmanaged
    {
        MoveWithAddedComponent(entity, component, overwriteExisting: false);
    }

    public void Set<T>(Entity entity, in T component)
        where T : unmanaged
    {
        var typeId = _componentTypes.GetId<T>();
        var record = GetRecord(entity);
        if (record.Archetype!.HasComponent(typeId))
        {
            record.Archetype.WriteComponent(record.ChunkIndex, record.RowIndex, typeId, component);
            return;
        }

        MoveWithAddedComponent(entity, component, overwriteExisting: true);
    }

    public bool Has<T>(Entity entity)
        where T : unmanaged
    {
        ValidateEntity(entity);
        var typeId = _componentTypes.GetId<T>();
        return _entityRecords[entity.Id].Archetype!.HasComponent(typeId);
    }

    public bool Remove<T>(Entity entity)
        where T : unmanaged
    {
        ValidateEntity(entity);
        var typeId = _componentTypes.GetId<T>();
        var record = _entityRecords[entity.Id];
        if (!record.Archetype!.HasComponent(typeId))
        {
            return false;
        }

        var transition = record.Archetype.GetRemoveTransition(typeId);
        var destination = transition.Destination;
        var slot = destination.AddEntity(entity);
        transition.Copy(record.ChunkIndex, record.RowIndex, slot);
        var removed = record.Archetype.RemoveAt(record.ChunkIndex, record.RowIndex);
        UpdateMovedEntity(removed);

        record.Archetype = destination;
        record.ChunkIndex = slot.ChunkIndex;
        record.RowIndex = slot.RowIndex;
        _entityRecords[entity.Id] = record;
        RefreshAccessors(entity.Id, record);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(Entity entity)
        where T : unmanaged
    {
        ValidateEntity(entity);
        var typeId = _componentTypes.GetId<T>();
#if NET8_0_OR_GREATER
        ref var record = ref CollectionsMarshal.AsSpan(_entityRecords)[entity.Id];
#else
        var record = _entityRecords[entity.Id];
#endif
        return ref record.Archetype!.GetComponentRef<T>(record.ChunkIndex, record.RowIndex, typeId);
    }

    public bool TryGet<T>(Entity entity, out T value)
        where T : unmanaged
    {
        if (!IsAlive(entity))
        {
            value = default;
            return false;
        }

        var typeId = _componentTypes.GetId<T>();
        var record = _entityRecords[entity.Id];
        if (!record.Archetype!.HasComponent(typeId))
        {
            value = default;
            return false;
        }

        value = record.Archetype.ReadComponent<T>(record.ChunkIndex, record.RowIndex, typeId);
        return true;
    }

    public Query<T1> Query<T1>()
        where T1 : unmanaged
    {
        unsafe
        {
            var include = stackalloc int[1];
            include[0] = _componentTypes.GetOrCreate<T1>().Id;
            return new Query<T1>(this, GetOrCreateQueryCache(new QueryCacheKey(ComponentSetKey.Create(include, 1), ComponentSetKey.Empty)));
        }
    }

    public Query<T1, T2> Query<T1, T2>()
        where T1 : unmanaged
        where T2 : unmanaged
    {
        unsafe
        {
            var include = stackalloc int[2];
            include[0] = _componentTypes.GetOrCreate<T1>().Id;
            include[1] = _componentTypes.GetOrCreate<T2>().Id;
            return new Query<T1, T2>(this, GetOrCreateQueryCache(new QueryCacheKey(ComponentSetKey.Create(include, 2), ComponentSetKey.Empty)));
        }
    }

    public Query<T1, T2, T3> Query<T1, T2, T3>()
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
    {
        unsafe
        {
            var include = stackalloc int[3];
            include[0] = _componentTypes.GetOrCreate<T1>().Id;
            include[1] = _componentTypes.GetOrCreate<T2>().Id;
            include[2] = _componentTypes.GetOrCreate<T3>().Id;
            return new Query<T1, T2, T3>(this, GetOrCreateQueryCache(new QueryCacheKey(ComponentSetKey.Create(include, 3), ComponentSetKey.Empty)));
        }
    }

    public QueryBuilder QueryBuilder() => new(this);

    public void AddSystem(SystemStage stage, Action<World> system) => _systems[stage].Add(system);

    public void Run(SystemStage stage)
    {
        var systems = _systems[stage];
        for (var i = 0; i < systems.Count; i++)
        {
            systems[i](this);
        }
    }

    public void RunAll()
    {
        Run(SystemStage.PreUpdate);
        Run(SystemStage.Update);
        Run(SystemStage.PostUpdate);
    }

    internal ComponentType GetComponentType<T>()
        where T : unmanaged
        => _componentTypes.GetOrCreate<T>();

    internal QueryCacheEntry GetOrCreateQueryCache(QueryCacheKey key)
    {
        if (_queryCache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var entry = new QueryCacheEntry(key);
        for (var i = 0; i < _archetypeList.Count; i++)
        {
            if (key.Matches(_archetypeList[i].Key))
            {
                entry.Matches.Add(_archetypeList[i]);
            }
        }

        _queryCache.Add(key, entry);
        return entry;
    }

    internal void RegisterArchetype(Archetype archetype)
    {
        _archetypes.Add(archetype.Key, archetype);
        _archetypeList.Add(archetype);

        foreach (var pair in _queryCache)
        {
            if (pair.Key.Matches(archetype.Key))
            {
                pair.Value.Matches.Add(archetype);
            }
        }
    }

    internal Archetype GetArchetype(ComponentSetKey key)
    {
        if (_archetypes.TryGetValue(key, out var archetype))
        {
            return archetype;
        }

        archetype = new Archetype(this, key, _componentTypes.ResolveAll(key.TypeIds));
        RegisterArchetype(archetype);
        return archetype;
    }

    private void MoveWithAddedComponent<T>(Entity entity, in T component, bool overwriteExisting)
        where T : unmanaged
    {
        ValidateEntity(entity);
        var typeId = _componentTypes.GetId<T>();
        var record = _entityRecords[entity.Id];
        var source = record.Archetype!;

        if (source.HasComponent(typeId))
        {
            if (!overwriteExisting)
            {
                throw new InvalidOperationException($"Entity {entity} already has component {typeof(T).Name}.");
            }

            source.WriteComponent(record.ChunkIndex, record.RowIndex, typeId, component);
            return;
        }

        var transition = source.GetAddTransition(typeId);
        var destination = transition.Destination;
        var slot = destination.AddEntity(entity);
        transition.Copy(record.ChunkIndex, record.RowIndex, slot);
        destination.WriteComponent(slot.ChunkIndex, slot.RowIndex, typeId, component);
        var removed = source.RemoveAt(record.ChunkIndex, record.RowIndex);
        UpdateMovedEntity(removed);

        record.Archetype = destination;
        record.ChunkIndex = slot.ChunkIndex;
        record.RowIndex = slot.RowIndex;
        _entityRecords[entity.Id] = record;
        RefreshAccessors(entity.Id, record);
    }

    private EntityRecord GetRecord(Entity entity)
    {
        ValidateEntity(entity);
        return _entityRecords[entity.Id];
    }

    private void UpdateMovedEntity(Archetype.RemovalResult removed)
    {
        if (removed.MovedEntityId < 0)
        {
            return;
        }

        var moved = _entityRecords[removed.MovedEntityId];
        if (!ReferenceEquals(moved.Archetype, removed.Source))
        {
            return;
        }

        moved.Archetype = removed.Source;
        moved.ChunkIndex = removed.MovedChunkIndex;
        moved.RowIndex = removed.MovedRowIndex;
        _entityRecords[removed.MovedEntityId] = moved;
        RefreshAccessors(removed.MovedEntityId, moved);
    }

    private void RefreshAccessors(int id, EntityRecord record)
    {
        for (var i = 0; i < _accessors.Count; i++) _accessors[i].Update(id, record);
    }

    [Conditional("DEBUG")]
    private void ValidateEntity(Entity entity)
    {
        if (_options.DiagnosticsEnabled && !IsAlive(entity))
        {
            throw new InvalidOperationException($"Entity {entity} is not alive in this world.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (var i = 0; i < _archetypeList.Count; i++)
        {
            _archetypeList[i].Dispose();
        }

        _disposed = true;
        foreach (var accessor in _accessors) accessor.Invalidate();
    }

    internal readonly struct EntitySlot
    {
        public EntitySlot(int chunkIndex, int rowIndex)
        {
            ChunkIndex = chunkIndex;
            RowIndex = rowIndex;
        }

        public int ChunkIndex { get; }

        public int RowIndex { get; }
    }

    internal struct EntityRecord
    {
        public int Version;
        public bool Alive;
        public Archetype? Archetype;
        public int ChunkIndex;
        public int RowIndex;
    }
}
