namespace SECS;

/// <summary>Retained storage; native bytes exclude managed arrays, metadata, and allocator overhead.</summary>
public readonly struct StorageStatistics
{
    internal StorageStatistics(int archetypes, int chunks, int entities, long nativeBytes, long accessorBytes)
    { ArchetypeCount = archetypes; ChunkCount = chunks; EntityCount = entities; NativeBytes = nativeBytes; AccessorBufferBytes = accessorBytes; }

    public int ArchetypeCount { get; }
    public int ChunkCount { get; }
    public int EntityCount { get; }
    public long NativeBytes { get; }
    /// <summary>Accessor address/version array payloads, excluding array headers and other managed storage.</summary>
    public long AccessorBufferBytes { get; }
}
