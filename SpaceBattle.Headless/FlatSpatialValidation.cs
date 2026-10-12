using System.Numerics;
using SpaceBattle;

internal static class FlatSpatialValidation
{
    // Main integration adds this call to Validation.Run; no caller files are changed here.
    public static void Run()
    {
        LayoutChecks();
        PointChecks();
        BoundsChecks();
        AllocationChecks();
        Console.WriteLine("PASS: flat-grid legacy candidate/exact-bounds oracles, boundaries, extremes, duplicate IDs, interleaving, Seal/Clear and reserved allocations.");
    }

    private static void LayoutChecks()
    {
        var layout = new FlatGridLayout(-2, -3, 4, 5);
        Check(layout.MetadataBytes == 4 * 5 * 16, "metadata uses four ints per cell");
        Check(layout.TryCell(-2, -3, out var first) && first == 0, "origin normalizes to zero");
        Check(layout.TryCell(1, 1, out var last) && last == 19, "row-major x + y * width");
        Check(!layout.TryCell(int.MinValue, int.MaxValue, out _), "extreme coordinates cannot overflow normalization");
        Check(new FlatGridLayout().MetadataBytes == 256 * 1024, "default metadata is 256 KiB");
        Check(new FlatGridLayout(-128, -128, 256, 256).MetadataBytes == 1024 * 1024, "maximum metadata cap");
        Reject(() => new FlatGridLayout(0, 0, 257, 256));
        Reject(() => new FlatGridLayout(int.MaxValue, 0, 2, 1));
        Reject(() => new FlatGridLayout(0, 0, 0, 1));
        layout.Reserve(8);
        layout.Add(first, 7); layout.Add(last, 8); layout.Add(first, 9);
        Check(Read(layout.Entries(first)).SequenceEqual(new[] { 9, 7 }), "append chains");
        layout.Seal();
        Check(Read(layout.Entries(first)).SequenceEqual(new[] { 9, 7 }), "contiguous sealed range");
        layout.Add(first, 10);
        Check(Read(layout.Entries(first)).SequenceEqual(new[] { 10, 9, 7 }), "add after Seal retains all members");
        layout.Seal(); layout.Seal();
        Check(Read(layout.Entries(last)).SequenceEqual(new[] { 8 }), "repeat Seal");
        var visited = new List<int>();
        foreach (var cell in layout.Query(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue)) visited.Add(cell);
        Check(visited.Order().SequenceEqual(new[] { first, last }), "huge query scans populated cells only");
        visited.Clear();
        foreach (var cell in layout.Query(int.MinValue, int.MinValue, -3, -4)) visited.Add(cell);
        Check(visited.Count == 0, "fully outside query has no normalized cells");
        layout.Clear(); layout.Seal();
        Check(layout.CellCount == 0 && !Read(layout.Entries(first)).Any(), "Clear removes sealed metadata");
        static int[] Read(FlatGridLayout.MemberQuery query)
        {
            var result = new List<int>();
            foreach (var entry in query) result.Add(entry);
            return result.ToArray();
        }
    }

    private static void PointChecks()
    {
        var grid = new SpatialPointIndex(-2, -2, 4, 4);
        var points = new List<(Vector2 Position, int Id)>(); var output = new List<int>();
        grid.Reserve(512);
        var coordinates = new[] { 0f, -0f, float.Epsilon, -float.Epsilon,
            -96f, MathF.BitDecrement(-96f), MathF.BitIncrement(-96f),
            -48f, MathF.BitDecrement(-48f), MathF.BitIncrement(-48f),
            48f, MathF.BitDecrement(48f), MathF.BitIncrement(48f),
            96f, MathF.BitDecrement(96f), MathF.BitIncrement(96f), -960f, 960f,
            float.MinValue, float.MaxValue, float.NaN, float.NegativeInfinity, float.PositiveInfinity };
        var radii = new[] { 0f, -0f, float.Epsilon, MathF.BitDecrement(48f), 48f,
            MathF.BitIncrement(48f), 96f, 960f, float.MaxValue, float.NaN, float.PositiveInfinity };
        var random = new Random(14812);
        for (var epoch = 0; epoch < 3; epoch++)
        {
            grid.Clear(); points.Clear();
            for (var i = 0; i < 240; i++)
            {
                var position = i < coordinates.Length ? new Vector2(coordinates[i], coordinates[(i * 7) % coordinates.Length])
                    : new Vector2(random.Next(-20000, 20001), random.Next(-20000, 20001));
                var id = i % 11 == 0 ? int.MinValue : 240 - i;
                grid.Add(position, id); points.Add((position, id));
                if (i % 17 == 0) grid.Seal();
                Compare(position, radii[i % radii.Length]); // immediate visibility after Add/Seal
                Compare(Vector2.Zero, i % 2 == 0 ? 48 : float.NaN);
            }
            grid.Seal();
            foreach (var x in coordinates)
                foreach (var radius in radii) Compare(new(x, x), radius);
            for (var i = 0; i < 500; i++) Compare(new(random.Next(-20000, 20001), random.Next(-20000, 20001)), radii[i % radii.Length]);
            grid.Add(Vector2.Zero, 999); points.Add((Vector2.Zero, 999));
            Compare(Vector2.Zero, 0); grid.Seal(); Compare(Vector2.Zero, 0);
            Compare(Vector2.Zero, float.NaN);
            // Negative radii must reject even if query coordinates are nonfinite.
            output.Add(123);
            Reject(() => grid.Query(new(float.NaN, 0), -1, output));
            Check(output.Count == 0, "point negative radius still clears output before rejecting");
            Reject(() => grid.Query(Vector2.Zero, float.NegativeInfinity, output));
        }
        grid.Clear(); points.Clear(); grid.Seal(); Compare(Vector2.Zero, float.NaN);
        Reject(() => grid.Reserve(-1));

        void Compare(Vector2 position, float radius)
        {
            grid.Query(position, radius, output);
            Check(output.SequenceEqual(PointOracle(points, position, radius)),
                $"point legacy candidate oracle epoch/query: ({position.X}, {position.Y}) radius={radius}");
        }
    }

    // Scalar full scan of legacy cells, independent of layout, chains and sealed ranges.
    // Preserve the legacy multiset: point callers use unique IDs, but repeated Add calls must remain visible.
    private static int[] PointOracle(List<(Vector2 Position, int Id)> points, Vector2 position, float radius)
    {
        var all = !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(radius);
        var minX = all ? 0 : PointCell(MathF.BitDecrement(position.X - radius));
        var minY = all ? 0 : PointCell(MathF.BitDecrement(position.Y - radius));
        var maxX = all ? 0 : PointCell(MathF.BitIncrement(position.X + radius));
        var maxY = all ? 0 : PointCell(MathF.BitIncrement(position.Y + radius));
        var result = new List<int>();
        foreach (var (point, id) in points)
        {
            if (all || !float.IsFinite(point.X) || !float.IsFinite(point.Y)) { result.Add(id); continue; }
            var x = PointCell(point.X); var y = PointCell(point.Y);
            if (x >= minX && x <= maxX && y >= minY && y <= maxY) result.Add(id);
        }
        return result.Order().ToArray();
    }

    private static int PointCell(float value) =>
        (int)Math.Clamp(Math.Floor((double)value / 48), int.MinValue, int.MaxValue);

    private static void BoundsChecks()
    {
        var grid = new SpatialBoundsIndex(-2, -2, 4, 4);
        var reference = new List<Box>(); var output = new List<int>();
        grid.Reserve(512);
        var coordinates = new[] { 0d, -0d, double.Epsilon, -double.Epsilon,
            -96d, Math.BitDecrement(-96d), Math.BitIncrement(-96d),
            -48d, Math.BitDecrement(-48d), Math.BitIncrement(-48d),
            48d, Math.BitDecrement(48d), Math.BitIncrement(48d), 96d,
            Math.BitDecrement(96d), Math.BitIncrement(96d), -1e300, 1e300, double.MinValue, double.MaxValue };
        var random = new Random(81248);
        for (var epoch = 0; epoch < 3; epoch++)
        {
            grid.Clear(); reference.Clear();
            Add(new(7, -1e300, -1e300, 1e300, 1e300));
            for (var i = 0; i < 240; i++)
            {
                var x = i < coordinates.Length ? coordinates[i] : random.Next(-20000, 20001);
                var y = i < coordinates.Length ? coordinates[(i * 7) % coordinates.Length] : random.Next(-20000, 20001);
                var size = i % 3 == 0 ? 0 : i % 3 == 1 ? 48 : 382;
                var box = new Box(i % 11 == 0 ? int.MaxValue : 240 - i, x, y, x + size, y + size);
                Add(box);
                if (i % 17 == 0) grid.Seal();
                Compare(box);
                Compare(new(0, 0, 0, 0, 0));
            }
            grid.Seal();
            foreach (var x in coordinates)
            {
                Compare(new(0, x, x, x, x));
                var previous = Math.BitDecrement(x);
                if (double.IsFinite(previous)) Compare(new(0, previous, previous, x, x));
            }
            Compare(new(0, double.MinValue, double.MinValue, double.MaxValue, double.MaxValue));
            for (var i = 0; i < 500; i++)
            {
                var x = random.Next(-20000, 20001); var y = random.Next(-20000, 20001);
                Compare(new(0, x, y, x + random.Next(1000), y + random.Next(1000)));
            }
            var from = new Vector2(-48, -48); var to = new Vector2(48, 48);
            grid.Add(-999, from, to, 3);
            reference.Add(Swept(-999, from, to, 3));
            grid.Query(from, to, 0, output);
            Check(output.SequenceEqual(BoundsOracle(reference, Swept(0, from, to, 0))), "swept outward padding after sealed insertion");
            grid.Seal(); Compare(new(0, -48, -48, -48, -48));
            typeof(SpatialBoundsIndex).GetField("generation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(grid, uint.MaxValue);
            Compare(new(0, -48, -48, -48, -48));
            var saved = output.ToArray();
            Reject(() => grid.QueryBounds(double.NaN, 0, 1, 1, output));
            Check(output.SequenceEqual(saved), "invalid bounds query preserves prior output");
            Reject(() => grid.AddBounds(1, 2, 0, 1, 1));
            Reject(() => grid.AddBounds(1, 0, 0, double.PositiveInfinity, 1));
            Reject(() => grid.Add(1, new(float.NaN, 0), Vector2.Zero, 1));
            Reject(() => grid.Query(Vector2.Zero, Vector2.Zero, float.NaN, output));
        }
        grid.Clear(); reference.Clear(); grid.Seal(); Compare(new(0, -1e300, -1e300, 1e300, 1e300));
        var dense = new SpatialBoundsIndex(); dense.Reserve(2);
        dense.AddBounds(1, 1, 1, 383, 383);
        Check(dense.CellCount == 64, "64-cell dense insertion");
        dense.Clear(); dense.AddBounds(1, 1, 1, 431, 431);
        Check(dense.CellCount == 0, "65+ cells bypass grid");
        dense.Seal(); dense.QueryBounds(430, 430, 431, 431, output);
        Check(output.SequenceEqual(new[] { 1 }), "oversized bounds remain queryable when sealed");

        void Add(Box box)
        {
            grid.AddBounds(box.Id, box.MinX, box.MinY, box.MaxX, box.MaxY); reference.Add(box);
        }
        void Compare(Box query)
        {
            grid.QueryBounds(query.MinX, query.MinY, query.MaxX, query.MaxY, output);
            Check(output.SequenceEqual(BoundsOracle(reference, query)), "independent closed-AABB oracle");
        }
    }

    private static int[] BoundsOracle(List<Box> entries, Box query) => entries
        .Where(e => e.MinX <= query.MaxX && e.MaxX >= query.MinX && e.MinY <= query.MaxY && e.MaxY >= query.MinY)
        .Select(e => e.Id).Distinct().Order().ToArray();

    private static Box Swept(int id, Vector2 from, Vector2 to, float radius) => new(id,
        Math.BitDecrement((double)Math.Min(from.X, to.X) - radius), Math.BitDecrement((double)Math.Min(from.Y, to.Y) - radius),
        Math.BitIncrement((double)Math.Max(from.X, to.X) + radius), Math.BitIncrement((double)Math.Max(from.Y, to.Y) + radius));

    private static void AllocationChecks()
    {
        var points = new SpatialPointIndex(); var bounds = new SpatialBoundsIndex();
        points.Reserve(256); bounds.Reserve(256);
        var pointOutput = new List<int>(256); var boundsOutput = new List<int>(256);
        for (var i = 0; i < 20; i++) Cycle(i);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 20; i < 220; i++) Cycle(i);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, $"reserved flat Add/Query/Seal/Clear allocations: {allocated}");

        void Cycle(int cycle)
        {
            points.Clear(); bounds.Clear();
            for (var i = 0; i < 256; i++)
            {
                var id = i % 13 == 0 ? -1 : cycle * 1000 + i;
                var offset = cycle % 3 == 0 ? (double)cycle * 960000 : cycle % 3 == 1 ? 0 : i * 48;
                var position = new Vector2((float)offset + 1, (float)-offset + 1);
                if (i % 31 == 0) position.X = float.NaN;
                points.Add(position, id);
                bounds.AddBounds(id, offset + 1, -offset + 1, offset + 383, -offset + 383);
                if (i % 64 == 0)
                {
                    points.Seal(); bounds.Seal();
                    points.Query(Vector2.Zero, 48, pointOutput);
                    bounds.QueryBounds(-48, -48, 48, 48, boundsOutput);
                }
            }
            points.Query(Vector2.Zero, float.NaN, pointOutput);
            bounds.QueryBounds(-1e300, -1e300, 1e300, 1e300, boundsOutput);
            points.Seal(); bounds.Seal();
            points.Query(new(float.MaxValue, float.MinValue), 0, pointOutput);
            bounds.QueryBounds(1e300, 1e300, 1e300, 1e300, boundsOutput);
        }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException("Expected ArgumentOutOfRangeException.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private readonly record struct Box(int Id, double MinX, double MinY, double MaxX, double MaxY);
}
