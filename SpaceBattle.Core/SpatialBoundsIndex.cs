using System.Numerics;
using System.Diagnostics;

namespace SpaceBattle;

internal sealed class SpatialBoundsIndex
{
    private readonly double cellSize;
    private readonly bool denseIds;
    private const int MaximumCellsPerBounds = 64;
    private readonly FlatGridLayout grid;
    private Entry[] entries = Array.Empty<Entry>();
    private int[] largeBounds = Array.Empty<int>();
    private readonly Dictionary<int, int>? stampIndexes;
    private uint[] stamps = Array.Empty<uint>();
    private int stampCount;
    private int entryCount;
    private int largeCount;
    private uint generation;
    private PhysicsGridStatistics diagnostics;
    internal bool CollectDiagnostics { get; set; }
    internal bool CollectTimings { get; set; }
    internal PhysicsGridStatistics Diagnostics => diagnostics;
    internal void ResetDiagnostics() => diagnostics = default;

    public SpatialBoundsIndex(int minimumCellX = -64, int minimumCellY = -64, int width = 128, int height = 128,
        double cellSize = 48, bool denseIds = false)
    {
        if (!double.IsFinite(cellSize) || cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
        this.cellSize = cellSize; this.denseIds = denseIds;
        stampIndexes = denseIds ? null : new();
        grid = new(minimumCellX, minimumCellY, width, height);
    }

    internal int CellCount => grid.CellCount;

    // Reserve outside Step/Sweep. Capacity counts Add/AddBounds calls between Clear calls.
    // Query's caller-owned output must also have capacity for the maximum unique ID count.
    public void Reserve(int objectCapacity)
    {
        if (objectCapacity < 0 || objectCapacity > int.MaxValue / MaximumCellsPerBounds)
            throw new ArgumentOutOfRangeException(nameof(objectCapacity));
        if (objectCapacity <= entries.Length) return;
        objectCapacity = (int)Math.Min(int.MaxValue / MaximumCellsPerBounds,
            Math.Max(objectCapacity, Math.Max(16L, (long)entries.Length * 2)));
        Array.Resize(ref entries, objectCapacity);
        Array.Resize(ref largeBounds, objectCapacity);
        Array.Resize(ref stamps, objectCapacity);
        grid.Reserve(objectCapacity * MaximumCellsPerBounds);
        stampIndexes?.EnsureCapacity(objectCapacity);
    }

    public void Clear()
    {
        var start = CollectTimings ? Stopwatch.GetTimestamp() : 0;
        if (CollectDiagnostics) { diagnostics.Clears++; diagnostics.CellsCleared += grid.CellCount; }
        grid.Clear();
        stampIndexes?.Clear();
        entryCount = largeCount = stampCount = 0;
        if (CollectTimings) diagnostics.ClearMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public void Seal()
    {
        var start = CollectTimings ? Stopwatch.GetTimestamp() : 0;
        grid.Seal();
        if (CollectDiagnostics) diagnostics.Rebuilds++;
        if (CollectTimings) diagnostics.SealMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public void Add(int id, Vector2 from, Vector2 to, float radius)
    {
        SweptBounds(from, to, radius, out var minX, out var minY, out var maxX, out var maxY);
        AddBounds(id, minX, minY, maxX, maxY);
    }

    public void AddBounds(int id, double minX, double minY, double maxX, double maxY)
    {
        var start = CollectTimings ? Stopwatch.GetTimestamp() : 0;
        ValidateBounds(minX, minY, maxX, maxY);
        if (entryCount == entries.Length) Reserve(checked(entryCount + 1));
        var entry = entryCount++;
        var stamp = id;
        if (denseIds)
        {
            if ((uint)id >= (uint)stamps.Length) throw new ArgumentOutOfRangeException(nameof(id));
        }
        else if (!stampIndexes!.TryGetValue(id, out stamp))
        {
            stamp = stampCount++;
            stampIndexes.Add(id, stamp);
            stamps[stamp] = 0;
        }
        entries[entry] = new(id, stamp, minX, minY, maxX, maxY);
        if (CollectDiagnostics) diagnostics.BoundsInserted++;
        CellBounds(minX, minY, maxX, maxY, out var left, out var bottom, out var right, out var top);
        // Do not clip insertion: the complete entry goes to the exact-bounds fallback.
        if (!Bounded(left, bottom, right, top, MaximumCellsPerBounds) || !grid.Contains(left, bottom, right, top))
        {
            largeBounds[largeCount++] = entry;
            if (CollectTimings) diagnostics.InsertMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            return;
        }
        var localLeft = (int)((long)left - grid.MinimumX); var localRight = (int)((long)right - grid.MinimumX);
        var localBottom = (int)((long)bottom - grid.MinimumY); var localTop = (int)((long)top - grid.MinimumY);
        for (var y = localBottom; y <= localTop; y++)
        {
            var row = y * grid.Width;
            for (var cell = row + localLeft; cell <= row + localRight; cell++)
            {
                grid.Add(cell, entry);
                if (CollectDiagnostics) diagnostics.MembershipsInserted++;
            }
        }
        if (CollectTimings) diagnostics.InsertMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public void Query(Vector2 from, Vector2 to, float radius, List<int> output, bool sort = true)
    {
        SweptBounds(from, to, radius, out var minX, out var minY, out var maxX, out var maxY);
        QueryBounds(minX, minY, maxX, maxY, output, sort);
    }

    public void QueryBounds(double minX, double minY, double maxX, double maxY, List<int> output, bool sort = true)
    {
        ArgumentNullException.ThrowIfNull(output);
        ValidateBounds(minX, minY, maxX, maxY);
        output.Clear();
        if (++generation == 0)
        {
            Array.Clear(stamps, 0, denseIds ? stamps.Length : stampCount);
            generation = 1;
        }
        CellBounds(minX, minY, maxX, maxY, out var left, out var bottom, out var right, out var top);
        foreach (var cell in grid.Query(left, bottom, right, top))
            foreach (var entry in grid.Entries(cell)) CollectEntry(entry, minX, minY, maxX, maxY, output);
        for (var i = 0; i < largeCount; i++) CollectEntry(largeBounds[i], minX, minY, maxX, maxY, output);
        if (sort)
        {
            output.Sort();
            if (CollectDiagnostics) diagnostics.Sorts++;
        }
    }

    private void CollectEntry(int index, double minX, double minY, double maxX, double maxY, List<int> output)
    {
        var entry = entries[index];
        if (CollectDiagnostics) diagnostics.MembershipsVisited++;
        if (entry.MinX > maxX || entry.MaxX < minX || entry.MinY > maxY || entry.MaxY < minY) return;
        if (stamps[entry.Stamp] == generation) return;
        stamps[entry.Stamp] = generation;
        output.Add(entry.Id);
        if (CollectDiagnostics) diagnostics.UniqueCandidates++;
    }

    private static bool Bounded(int left, int bottom, int right, int top, int limit) =>
        ((double)right - left + 1) * ((double)top - bottom + 1) <= limit;

    private void CellBounds(double minX, double minY, double maxX, double maxY,
        out int left, out int bottom, out int right, out int top)
    {
        left = Coordinate(Math.BitDecrement(minX)); bottom = Coordinate(Math.BitDecrement(minY));
        right = Coordinate(Math.BitIncrement(maxX)); top = Coordinate(Math.BitIncrement(maxY));
    }

    private int Coordinate(double value) => (int)Math.Clamp(Math.Floor(value / cellSize), int.MinValue, int.MaxValue);

    internal static void SweptBounds(Vector2 from, Vector2 to, float radius,
        out double minX, out double minY, out double maxX, out double maxY)
    {
        if (!float.IsFinite(from.X) || !float.IsFinite(from.Y)) throw new ArgumentOutOfRangeException(nameof(from));
        if (!float.IsFinite(to.X) || !float.IsFinite(to.Y)) throw new ArgumentOutOfRangeException(nameof(to));
        if (!float.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        minX = Math.BitDecrement((double)Math.Min(from.X, to.X) - radius);
        minY = Math.BitDecrement((double)Math.Min(from.Y, to.Y) - radius);
        maxX = Math.BitIncrement((double)Math.Max(from.X, to.X) + radius);
        maxY = Math.BitIncrement((double)Math.Max(from.Y, to.Y) + radius);
    }

    private static void ValidateBounds(double minX, double minY, double maxX, double maxY)
    {
        if (!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(maxX) || !double.IsFinite(maxY)
            || minX > maxX || minY > maxY) throw new ArgumentOutOfRangeException(nameof(minX), "Bounds must be finite and ordered.");
    }

    private readonly record struct Entry(int Id, int Stamp, double MinX, double MinY, double MaxX, double MaxY);
}
