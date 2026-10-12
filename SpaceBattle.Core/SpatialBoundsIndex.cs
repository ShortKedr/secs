using System.Numerics;

namespace SpaceBattle;

internal sealed class SpatialBoundsIndex
{
    private const double CellSize = 48;
    private const int MaximumCellsPerBounds = 64;
    private readonly Dictionary<(int X, int Y), int> cells = new();
    private Entry[] entries = Array.Empty<Entry>();
    private int[] membershipEntries = Array.Empty<int>();
    private int[] membershipNext = Array.Empty<int>();
    private int[] largeBounds = Array.Empty<int>();
    private readonly Dictionary<int, uint> stamps = new();
    private int entryCount;
    private int membershipCount;
    private int largeCount;
    private uint generation;

    internal int CellCount => cells.Count;

    // Reserve outside Step/Sweep. Capacity counts Add/AddBounds calls between Clear calls.
    // Query's caller-owned output must also have capacity for the maximum unique ID count.
    public void Reserve(int objectCapacity)
    {
        if (objectCapacity < 0 || objectCapacity > int.MaxValue / MaximumCellsPerBounds)
            throw new ArgumentOutOfRangeException(nameof(objectCapacity));
        if (objectCapacity <= entries.Length) return;
        objectCapacity = (int)Math.Min(int.MaxValue / MaximumCellsPerBounds,
            Math.Max(objectCapacity, Math.Max(16L, (long)entries.Length * 2)));
        var memberships = objectCapacity * MaximumCellsPerBounds;
        Array.Resize(ref entries, objectCapacity);
        Array.Resize(ref largeBounds, objectCapacity);
        Array.Resize(ref membershipEntries, memberships);
        Array.Resize(ref membershipNext, memberships);
        cells.EnsureCapacity(memberships);
        stamps.EnsureCapacity(objectCapacity);
    }

    public void Clear()
    {
        cells.Clear(); stamps.Clear(); entryCount = membershipCount = largeCount = 0;
    }

    public void Add(int id, Vector2 from, Vector2 to, float radius)
    {
        SweptBounds(from, to, radius, out var minX, out var minY, out var maxX, out var maxY);
        AddBounds(id, minX, minY, maxX, maxY);
    }

    public void AddBounds(int id, double minX, double minY, double maxX, double maxY)
    {
        ValidateBounds(minX, minY, maxX, maxY);
        if (entryCount == entries.Length) Reserve(Math.Max(16, checked(entryCount * 2)));
        var entry = entryCount++;
        entries[entry] = new(id, minX, minY, maxX, maxY);
        stamps.TryAdd(id, 0);
        CellBounds(minX, minY, maxX, maxY, out var left, out var bottom, out var right, out var top);
        if (!Bounded(left, bottom, right, top, MaximumCellsPerBounds))
        {
            largeBounds[largeCount++] = entry;
            return;
        }
        for (long x = left; x <= right; x++)
            for (long y = bottom; y <= top; y++)
            {
                var key = ((int)x, (int)y);
                membershipEntries[membershipCount] = entry;
                membershipNext[membershipCount] = cells.TryGetValue(key, out var head) ? head : -1;
                cells[key] = membershipCount++;
            }
    }

    public void Query(Vector2 from, Vector2 to, float radius, List<int> output)
    {
        SweptBounds(from, to, radius, out var minX, out var minY, out var maxX, out var maxY);
        QueryBounds(minX, minY, maxX, maxY, output);
    }

    public void QueryBounds(double minX, double minY, double maxX, double maxY, List<int> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        ValidateBounds(minX, minY, maxX, maxY);
        output.Clear();
        if (++generation == 0)
        {
            // Rebuild current ID stamps only on wrap; capacity is retained.
            stamps.Clear();
            for (var i = 0; i < entryCount; i++) stamps.TryAdd(entries[i].Id, 0);
            generation = 1;
        }
        CellBounds(minX, minY, maxX, maxY, out var left, out var bottom, out var right, out var top);
        if (Bounded(left, bottom, right, top, Math.Min(MaximumCellsPerBounds, cells.Count)))
        {
            for (long x = left; x <= right; x++)
                for (long y = bottom; y <= top; y++)
                    if (cells.TryGetValue(((int)x, (int)y), out var head))
                        Collect(head, minX, minY, maxX, maxY, output);
        }
        else
            foreach (var (cell, head) in cells)
                if (cell.X >= left && cell.X <= right && cell.Y >= bottom && cell.Y <= top)
                    Collect(head, minX, minY, maxX, maxY, output);
        for (var i = 0; i < largeCount; i++) CollectEntry(largeBounds[i], minX, minY, maxX, maxY, output);
        output.Sort();
    }

    private void Collect(int head, double minX, double minY, double maxX, double maxY, List<int> output)
    {
        for (var member = head; member != -1; member = membershipNext[member])
            CollectEntry(membershipEntries[member], minX, minY, maxX, maxY, output);
    }

    private void CollectEntry(int index, double minX, double minY, double maxX, double maxY, List<int> output)
    {
        var entry = entries[index];
        if (entry.MinX > maxX || entry.MaxX < minX || entry.MinY > maxY || entry.MaxY < minY) return;
        if (stamps[entry.Id] == generation) return;
        stamps[entry.Id] = generation;
        output.Add(entry.Id);
    }

    private static bool Bounded(int left, int bottom, int right, int top, int limit) =>
        ((double)right - left + 1) * ((double)top - bottom + 1) <= limit;

    private static void CellBounds(double minX, double minY, double maxX, double maxY,
        out int left, out int bottom, out int right, out int top)
    {
        left = Coordinate(Math.BitDecrement(minX)); bottom = Coordinate(Math.BitDecrement(minY));
        right = Coordinate(Math.BitIncrement(maxX)); top = Coordinate(Math.BitIncrement(maxY));
    }

    private static int Coordinate(double value) => (int)Math.Clamp(Math.Floor(value / CellSize), int.MinValue, int.MaxValue);

    private static void SweptBounds(Vector2 from, Vector2 to, float radius,
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

    private readonly record struct Entry(int Id, double MinX, double MinY, double MaxX, double MaxY);
}
