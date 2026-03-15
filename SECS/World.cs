using System;
using System.Collections.Generic;
using System.Diagnostics;

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
    {
        var id = _freeEntityIds.Count > 0 ? _freeEntityIds.Pop() : _entityRecords.Count;
        var version = id < _entityRecords.Count ? _entityRecords[id].Version : 1;

        if (id == _entityRecords.Count)
        {
            _entityRecords.Add(default);
        }

        var record = _entityRecords[id];
        record.Version = version;
        record.Alive = true;

        var slot = _rootArchetype.AddEntity(new Entity(id, version));
        record.Archetype = _rootArchetype;
        record.ChunkIndex = slot.ChunkIndex;
        record.RowIndex = slot.RowIndex;
        _entityRecords[id] = record;

        return new Entity(id, version);
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
        var type = _componentTypes.GetOrCreate<T>();
        var record = GetRecord(entity);
        if (record.Archetype!.HasComponent(type.Id))
        {
            record.Archetype.WriteComponent(record.ChunkIndex, record.RowIndex, type.Id, component);
            return;
        }

        MoveWithAddedComponent(entity, component, overwriteExisting: true);
    }

    public bool Has<T>(Entity entity)
        where T : unmanaged
    {
        ValidateEntity(entity);
        var type = _componentTypes.GetOrCreate<T>();
        return _entityRecords[entity.Id].Archetype!.HasComponent(type.Id);
    }

    public bool Remove<T>(Entity entity)
        where T : unmanaged
    {
        ValidateEntity(entity);
        var type = _componentTypes.GetOrCreate<T>();
        var record = _entityRecords[entity.Id];
        if (!record.Archetype!.HasComponent(type.Id))
        {
            return false;
        }

        var destination = GetArchetype(record.Archetype.GetRemoveKey(type.Id));
        var slot = destination.AddEntity(entity);
        record.Archetype.CopyToWithout(record.ChunkIndex, record.RowIndex, destination, slot, type.Id);
        var removed = record.Archetype.RemoveAt(record.ChunkIndex, record.RowIndex);
        UpdateMovedEntity(removed);

        record.Archetype = destination;
        record.ChunkIndex = slot.ChunkIndex;
        record.RowIndex = slot.RowIndex;
        _entityRecords[entity.Id] = record;
        return true;
    }

    public ref T Get<T>(Entity entity)
        where T : unmanaged
    {
        ValidateEntity(entity);
        var type = _componentTypes.GetOrCreate<T>();
        var record = _entityRecords[entity.Id];
        return ref record.Archetype!.GetComponentRef<T>(record.ChunkIndex, record.RowIndex, type.Id);
    }

    public bool TryGet<T>(Entity entity, out T value)
        where T : unmanaged
    {
        if (!IsAlive(entity))
        {
            value = default;
            return false;
        }

        var type = _componentTypes.GetOrCreate<T>();
        var record = _entityRecords[entity.Id];
        if (!record.Archetype!.HasComponent(type.Id))
        {
            value = default;
            return false;
        }

        value = record.Archetype.ReadComponent<T>(record.ChunkIndex, record.RowIndex, type.Id);
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

    private Archetype GetArchetype(ComponentSetKey key)
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
        var type = _componentTypes.GetOrCreate<T>();
        var record = _entityRecords[entity.Id];
        var source = record.Archetype!;

        if (source.HasComponent(type.Id))
        {
            if (!overwriteExisting)
            {
                throw new InvalidOperationException($"Entity {entity} already has component {type.RuntimeType.Name}.");
            }

            source.WriteComponent(record.ChunkIndex, record.RowIndex, type.Id, component);
            return;
        }

        var destination = GetArchetype(source.GetAddKey(type.Id));
        var slot = destination.AddEntity(entity);
        source.CopyToWith(record.ChunkIndex, record.RowIndex, destination, slot, type.Id, component);
        var removed = source.RemoveAt(record.ChunkIndex, record.RowIndex);
        UpdateMovedEntity(removed);

        record.Archetype = destination;
        record.ChunkIndex = slot.ChunkIndex;
        record.RowIndex = slot.RowIndex;
        _entityRecords[entity.Id] = record;
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
