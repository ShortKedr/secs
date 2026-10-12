namespace SpaceBattle;

// Snapshot-local values, indexed by recycled public ID, never by global Serial.
internal sealed class EntityIndexTable
{
    private const int MaximumDenseEntries = 1 << 20; // 12 MiB payload cap; unusual IDs use the sparse fallback.
    private Entry[] entries = [];
    private readonly Dictionary<EntityKey, int> outside = new();
    private uint epoch = 1;
    private struct Entry { public int Generation, Value; public uint Epoch; }

    public void Clear()
    {
        outside.Clear();
        if (++epoch == 0) { Array.Clear(entries); epoch = 1; }
    }

    public bool TryGetValue(EntityKey key, out int value)
    {
        if ((uint)key.Id < (uint)entries.Length)
        {
            ref readonly var entry = ref entries[key.Id];
            if (entry.Epoch == epoch && entry.Generation == key.Generation) { value = entry.Value; return true; }
        }
        else if (key.Id >= MaximumDenseEntries) return outside.TryGetValue(key, out value);
        value = default; return false;
    }

    public int this[EntityKey key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();

    public void Add(EntityKey key, int value)
    {
        if (key.Id < 0) throw new ArgumentOutOfRangeException(nameof(key));
        if (key.Id >= MaximumDenseEntries) { outside.Add(key, value); return; }
        if (key.Id >= entries.Length)
            Array.Resize(ref entries, Math.Min(MaximumDenseEntries, Math.Max(key.Id + 1, Math.Max(64, entries.Length * 2))));
        ref var entry = ref entries[key.Id];
        if (entry.Epoch == epoch && entry.Generation == key.Generation) throw new ArgumentException("Duplicate entity.", nameof(key));
        entry = new() { Generation = key.Generation, Value = value, Epoch = epoch };
    }

    public void Remove(EntityKey key)
    {
        if ((uint)key.Id < (uint)entries.Length)
        {
            ref var entry = ref entries[key.Id];
            if (entry.Epoch == epoch && entry.Generation == key.Generation) entry.Epoch = 0;
        }
        else outside.Remove(key);
    }
}
