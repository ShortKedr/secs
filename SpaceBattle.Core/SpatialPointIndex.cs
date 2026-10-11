using System.Numerics;

namespace SpaceBattle;

internal sealed class SpatialPointIndex
{
    private const float CellSize = 48;
    private readonly Dictionary<(int X, int Y), List<int>> cells = new();
    private readonly List<List<int>> buffers = new();
    private readonly List<int> nonfinite = new();
    private int usedBuffers;

    public void Clear()
    {
        cells.Clear(); nonfinite.Clear(); usedBuffers = 0;
    }

    public void Add(Vector2 position, int index)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)) { nonfinite.Add(index); return; }
        var cell = (Coordinate(position.X), Coordinate(position.Y));
        if (!cells.TryGetValue(cell, out var bucket))
        {
            if (usedBuffers == buffers.Count) buffers.Add(new());
            bucket = buffers[usedBuffers++]; bucket.Clear(); cells.Add(cell, bucket);
        }
        bucket.Add(index);
    }

    public void Query(Vector2 position, float radius, List<int> output)
    {
        output.Clear();
        if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(radius))
        {
            foreach (var bucket in cells.Values) output.AddRange(bucket);
        }
        else
        {
            // Round outward so a float distance test at the boundary cannot lose a candidate.
            var minX = Coordinate(MathF.BitDecrement(position.X - radius));
            var maxX = Coordinate(MathF.BitIncrement(position.X + radius));
            var minY = Coordinate(MathF.BitDecrement(position.Y - radius));
            var maxY = Coordinate(MathF.BitIncrement(position.Y + radius));
            var width = (long)maxX - minX + 1; var height = (long)maxY - minY + 1;
            if ((double)width * height > cells.Count)
            {
                foreach (var (cell, bucket) in cells)
                    if (cell.X >= minX && cell.X <= maxX && cell.Y >= minY && cell.Y <= maxY) output.AddRange(bucket);
            }
            else
                for (long x = minX; x <= maxX; x++)
                    for (long y = minY; y <= maxY; y++)
                        if (cells.TryGetValue(((int)x, (int)y), out var bucket)) output.AddRange(bucket);
        }
        output.AddRange(nonfinite);
        output.Sort();
    }

    private static int Coordinate(float value) => (int)Math.Clamp(Math.Floor((double)value / CellSize), int.MinValue, int.MaxValue);
}
