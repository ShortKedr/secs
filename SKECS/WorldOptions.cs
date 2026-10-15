namespace SKECS;

public readonly struct WorldOptions
{
    public WorldOptions() : this(256, false) { }

    public WorldOptions(int chunkCapacity = 256, bool diagnosticsEnabled = false)
    {
        ChunkCapacity = chunkCapacity <= 0 ? 256 : chunkCapacity;
        DiagnosticsEnabled = diagnosticsEnabled;
    }

    public int ChunkCapacity { get; }

    public bool DiagnosticsEnabled { get; }
}
