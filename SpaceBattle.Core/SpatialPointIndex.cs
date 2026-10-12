using System.Numerics;

namespace SpaceBattle;

internal sealed class SpatialPointIndex
{
    private const float CellSize = 48;
    private readonly FlatGridLayout grid;
    private Entry[] entries = Array.Empty<Entry>();
    private int[] outside = Array.Empty<int>();
    private int entryCount, outsideCount;

    public SpatialPointIndex(int minimumCellX = -64, int minimumCellY = -64, int width = 128, int height = 128) =>
        grid = new(minimumCellX, minimumCellY, width, height);

    // Capacity and caller-owned output capacity count Add calls, including duplicate IDs.
    // Reserve belongs outside the hot loop.
    public void Reserve(int pointCapacity)
    {
        if (pointCapacity < 0 || pointCapacity > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(pointCapacity));
        if (pointCapacity <= entries.Length) return;
        pointCapacity = (int)Math.Min(Array.MaxLength, Math.Max(pointCapacity, Math.Max(16L, (long)entries.Length * 2)));
        Array.Resize(ref entries, pointCapacity);
        Array.Resize(ref outside, pointCapacity);
        grid.Reserve(pointCapacity);
    }

    public void Clear()
    {
        grid.Clear(); entryCount = outsideCount = 0;
    }

    public void Seal() => grid.Seal();

    public void Add(Vector2 position, int index)
    {
        if (entryCount == entries.Length) Reserve(checked(entryCount + 1));
        var nonfinite = !float.IsFinite(position.X) || !float.IsFinite(position.Y);
        var x = nonfinite ? 0 : Coordinate(position.X); var y = nonfinite ? 0 : Coordinate(position.Y);
        var entry = entryCount++;
        entries[entry] = new(index, x, y, nonfinite);
        if (!nonfinite && grid.TryCell(x, y, out var cell)) grid.Add(cell, entry);
        else outside[outsideCount++] = entry;
    }

    public void Query(Vector2 position, float radius, List<int> output)
    {
        output.Clear();
        if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(radius))
        {
            for (var i = 0; i < entryCount; i++) output.Add(entries[i].Id);
        }
        else
        {
            // Keep the legacy float subtraction and outward rounding exactly.
            var minX = Coordinate(MathF.BitDecrement(position.X - radius));
            var maxX = Coordinate(MathF.BitIncrement(position.X + radius));
            var minY = Coordinate(MathF.BitDecrement(position.Y - radius));
            var maxY = Coordinate(MathF.BitIncrement(position.Y + radius));
            foreach (var cell in grid.Query(minX, minY, maxX, maxY))
                foreach (var entry in grid.Entries(cell)) output.Add(entries[entry].Id);
            for (var i = 0; i < outsideCount; i++)
            {
                var entry = entries[outside[i]];
                // Out-of-window finite points keep their exact legacy clamped cell, not an AABB filter.
                if (entry.Nonfinite || entry.X >= minX && entry.X <= maxX && entry.Y >= minY && entry.Y <= maxY)
                    output.Add(entry.Id);
            }
        }
        output.Sort();
    }

    private static int Coordinate(float value) => (int)Math.Clamp(Math.Floor((double)value / CellSize), int.MinValue, int.MaxValue);
    // Bounded tactical sampling, separate from the exact legacy Query contract.
    public void QueryLimited(Vector2 position, float radius, List<int> output, int limit, ref int work,
        ReadOnlySpan<bool> excluded = default, ReadOnlySpan<bool> ineligible = default)
    {
        output.Clear();
        if (radius < 0 || limit < 1 || work < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        var finite = float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(radius);
        if (!finite)
        {
            for (var i = 0; i < entryCount && work > 0 && output.Count < limit; i++, work--)
            {
                var id = entries[i].Id;
                if (!IsBlocked(id, excluded, ineligible)) output.Add(id);
            }
            output.Sort(); return;
        }
        var left = Coordinate(MathF.BitDecrement(position.X - radius));
        var right = Coordinate(MathF.BitIncrement(position.X + radius));
        var bottom = Coordinate(MathF.BitDecrement(position.Y - radius));
        var top = Coordinate(MathF.BitIncrement(position.Y + radius));
        var query = grid.QueryBounded(left, bottom, right, top, work);
        while (output.Count < limit && query.MoveNext())
            foreach (var entry in grid.ForwardEntries(query.Current))
            {
                if (!query.ConsumeWork()) break;
                var id = entries[entry].Id;
                if (IsBlocked(id, excluded, ineligible)) continue;
                output.Add(id);
                if (output.Count == limit) break;
            }
        work = query.RemainingWork;
        for (var i = 0; i < outsideCount && work > 0 && output.Count < limit; i++)
        {
            work--;
            var entry = entries[outside[i]];
            if (!IsBlocked(entry.Id, excluded, ineligible) &&
                (entry.Nonfinite || entry.X >= left && entry.X <= right && entry.Y >= bottom && entry.Y <= top))
                output.Add(entry.Id);
        }
        output.Sort();
    }
    private static bool IsBlocked(int id, ReadOnlySpan<bool> excluded, ReadOnlySpan<bool> ineligible) =>
        id >= 0 && (id < excluded.Length && excluded[id] || id < ineligible.Length && ineligible[id]);
    private readonly record struct Entry(int Id, int X, int Y, bool Nonfinite);
}
