namespace SECS;

/// <summary>A world-bound address cache, maintained on structural changes. Costs storage per entity ID.</summary>
/// <remarks>Pass handles from the owning world only. Returned refs must not survive structural changes.</remarks>
public readonly struct ComponentAccessor<T> where T : unmanaged
{
    private readonly AccessorStorage _storage;

    internal ComponentAccessor(AccessorStorage storage) => _storage = storage;

    public ref T Get(Entity entity) => ref _storage.Get<T>(entity);
    public bool Has(Entity entity) => _storage.Has(entity);
    public bool TryGet(Entity entity, out T value)
    {
        if (!Has(entity)) { value = default; return false; }
        value = Get(entity);
        return true;
    }
}
