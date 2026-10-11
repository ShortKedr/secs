namespace SpaceBattle;

// Only identity translation lives here. Components and query membership stay native.
public abstract class NativeStore<TNative> : IEcsStore where TNative : struct
{
    private struct Slot
    {
        public TNative Native;
        public int Generation;
        public bool Used;
    }

    private Slot[] slots = new Slot[64];
    private readonly Stack<int> free = new();
    private readonly Dictionary<TNative, EntityKey> keys;
    private int length;
    private int queryDepth;
    private bool disposed;

    protected NativeStore(IEqualityComparer<TNative>? comparer = null) => keys = new(comparer);

    public abstract string Name { get; }
    protected abstract TNative CreateNative();
    protected abstract bool AliveNative(TNative native);
    protected abstract void DestroyNative(TNative native);
    protected abstract void DisposeNative();

    protected void CheckOpen() => ObjectDisposedException.ThrowIf(disposed, this);
    protected void CheckMutation()
    {
        CheckOpen();
        if (queryDepth != 0)
            throw new InvalidOperationException("Structural changes must be queued until the query completes.");
    }
    protected void BeginQuery() { CheckOpen(); queryDepth++; }
    protected void EndQuery() => queryDepth--;
    protected EntityKey Key(TNative native) => keys.TryGetValue(native, out var key)
        ? key : throw new InvalidOperationException("Native entity has no adapter identity.");
    protected TNative Native(EntityKey key)
    {
        CheckOpen();
        if (!Alive(key)) throw new InvalidOperationException("Entity key is stale or invalid.");
        return slots[key.Id].Native;
    }

    public EntityKey Create()
    {
        CheckMutation();
        var native = CreateNative();
        int id;
        if (free.Count != 0) id = free.Pop();
        else
        {
            id = length++;
            if (id == slots.Length) Array.Resize(ref slots, checked(slots.Length * 2));
            slots[id].Generation = 1;
        }
        ref var slot = ref slots[id];
        slot.Native = native;
        slot.Used = true;
        var key = new EntityKey(id, slot.Generation);
        keys.Add(native, key);
        return key;
    }

    public bool Alive(EntityKey key) => !disposed && (uint)key.Id < (uint)length
        && slots[key.Id].Used && slots[key.Id].Generation == key.Generation
        && AliveNative(slots[key.Id].Native);

    public void Destroy(EntityKey key)
    {
        CheckMutation();
        if (!Alive(key)) return;
        ref var slot = ref slots[key.Id];
        DestroyNative(slot.Native);
        keys.Remove(slot.Native);
        slot.Native = default;
        slot.Used = false;
        // Never wrap a public generation back onto an old key.
        if (slot.Generation != int.MaxValue)
        {
            slot.Generation++;
            free.Push(key.Id);
        }
    }

    public abstract ref T Get<T>(EntityKey entity) where T : unmanaged;
    public abstract bool Has<T>(EntityKey entity) where T : unmanaged;
    public abstract void Set<T>(EntityKey entity, in T value) where T : unmanaged;
    public abstract void Remove<T>(EntityKey entity) where T : unmanaged;
    public virtual void Commit() => CheckMutation();
    public abstract void ForEach<T, P>(ref P processor) where T : unmanaged where P : struct, IProcessor<T>;
    public abstract void ForEach<A, B, P>(ref P processor) where A : unmanaged where B : unmanaged where P : struct, IProcessor<A, B>;
    public abstract void ForEach<A, B, C, P>(ref P processor) where A : unmanaged where B : unmanaged where C : unmanaged where P : struct, IProcessor<A, B, C>;
    public virtual StoreDiagnostics Diagnostics()
    {
        CheckOpen();
        // These vendors do not expose comparable storage byte/chunk counters.
        return new(keys.Count, null, null, null, null);
    }
    public void Dispose()
    {
        if (disposed) return;
        CheckMutation();
        DisposeNative();
        disposed = true;
        keys.Clear();
        free.Clear();
        slots = Array.Empty<Slot>();
    }
}
