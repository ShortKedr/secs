namespace SpaceBattle;

public readonly record struct EntityKey(int Id, int Generation)
{
    public static readonly EntityKey None = new(-1, 0);
    public bool IsNone => Id < 0;
}

public interface IProcessor<T> where T : unmanaged
{
    void Execute(EntityKey entity, ref T component);
}

public interface IProcessor<T1, T2> where T1 : unmanaged where T2 : unmanaged
{
    void Execute(EntityKey entity, ref T1 first, ref T2 second);
}

public interface IProcessor<T1, T2, T3> where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
{
    void Execute(EntityKey entity, ref T1 first, ref T2 second, ref T3 third);
}

// Each adapter uses its framework's native query, storage, and entity lifetime.
public interface IEcsStore : IDisposable
{
    string Name { get; }
    EntityKey Create();
    void Destroy(EntityKey entity);
    bool Alive(EntityKey entity);
    ref T Get<T>(EntityKey entity) where T : unmanaged;
    bool Has<T>(EntityKey entity) where T : unmanaged;
    void Set<T>(EntityKey entity, in T value) where T : unmanaged;
    void Remove<T>(EntityKey entity) where T : unmanaged;
    void Commit();
    void ForEach<T, P>(ref P processor) where T : unmanaged where P : struct, IProcessor<T>;
    void ForEach<T1, T2, P>(ref P processor) where T1 : unmanaged where T2 : unmanaged where P : struct, IProcessor<T1, T2>;
    void ForEach<T1, T2, T3, P>(ref P processor) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where P : struct, IProcessor<T1, T2, T3>;
    StoreDiagnostics Diagnostics();
}

public readonly record struct StoreDiagnostics(int Entities, int? Archetypes, int? Chunks, long? NativeBytes, long? AccessorBytes);
