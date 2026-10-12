namespace SpaceBattle;

// Cell coordinates are translated before indexing; no sparse cell dictionary is needed.
// The four ints per cell (including the occupied-cell list) are capped at 1 MiB.
internal sealed class FlatGridLayout
{
    internal const int MaximumCellCount = 65536;
    private readonly Cell[] cells;
    private readonly int[] occupied;
    private Member[] members = Array.Empty<Member>();
    private int[] snapshot = Array.Empty<int>();
    private int memberCount;
    private bool sealedSnapshot;

    internal int MinimumX { get; }
    internal int MinimumY { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal int CellCount { get; private set; }
    internal int MetadataBytes => cells.Length * 16;

    internal FlatGridLayout(int minimumX = -64, int minimumY = -64, int width = 128, int height = 128)
    {
        if (width <= 0 || height <= 0 || (long)width * height > MaximumCellCount
            || (long)minimumX + width - 1 > int.MaxValue || (long)minimumY + height - 1 > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), "Grid must fit the cell coordinate range and 1 MiB metadata budget.");
        MinimumX = minimumX; MinimumY = minimumY; Width = width; Height = height;
        cells = new Cell[width * height];
        occupied = new int[cells.Length];
    }

    internal void Reserve(int membershipCapacity)
    {
        if (membershipCapacity < 0) throw new ArgumentOutOfRangeException(nameof(membershipCapacity));
        if (membershipCapacity <= members.Length) return;
        Array.Resize(ref members, membershipCapacity);
        Array.Resize(ref snapshot, membershipCapacity);
    }

    internal void Clear()
    {
        for (var i = 0; i < CellCount; i++) cells[occupied[i]] = default;
        CellCount = memberCount = 0;
        sealedSnapshot = false;
    }

    internal bool Contains(int left, int bottom, int right, int top) =>
        left >= MinimumX && bottom >= MinimumY
        && (long)right - MinimumX < Width && (long)top - MinimumY < Height;

    internal bool TryCell(int x, int y, out int cell)
    {
        var localX = (long)x - MinimumX; var localY = (long)y - MinimumY;
        if (localX < 0 || localX >= Width || localY < 0 || localY >= Height) { cell = -1; return false; }
        cell = (int)localX + (int)localY * Width;
        return true;
    }

    internal void Add(int cellIndex, int entry)
    {
        ref var cell = ref cells[cellIndex];
        if (cell.Count == 0) occupied[CellCount++] = cellIndex;
        // Heads and links use index + 1 so zero-initialized metadata means empty.
        members[memberCount] = new(entry, cell.Head);
        cell.Head = ++memberCount;
        cell.Count++;
        sealedSnapshot = false;
    }

    // Explicit only: interleaved Add/Query callers must not pay a rebuild per query.
    // A subsequent Add falls back to the retained append chains until the next Seal.
    internal void Seal()
    {
        if (sealedSnapshot) return;
        var next = 0;
        for (var i = 0; i < CellCount; i++)
        {
            ref var cell = ref cells[occupied[i]];
            cell.Start = next;
            for (var head = cell.Head; head != 0; head = members[head - 1].Next)
                snapshot[next++] = members[head - 1].Entry;
        }
        sealedSnapshot = true;
    }

    internal CellQuery Query(int left, int bottom, int right, int top) => new(this, left, bottom, right, top);
    internal CellQuery QueryBounded(int left, int bottom, int right, int top, int work) => new(this, left, bottom, right, top, work);
    internal MemberQuery Entries(int cell) => new(this, cells[cell]);
    internal MemberQuery ForwardEntries(int cell)
    {
        if (!sealedSnapshot) throw new InvalidOperationException("Bounded snapshot query requires Seal.");
        return new(this, cells[cell], true);
    }
    internal struct Cell { internal int Head, Start, Count; }
    private readonly record struct Member(int Entry, int Next);

    internal struct MemberQuery
    {
        private readonly FlatGridLayout grid;
        private readonly bool useSnapshot;
        private readonly int end;
        private readonly int start;
        private readonly bool reverse;
        private int next;
        internal MemberQuery(FlatGridLayout grid, Cell cell, bool reverse = false)
        {
            this.grid = grid; useSnapshot = grid.sealedSnapshot;
            next = useSnapshot ? cell.Start : cell.Head;
            end = cell.Start + cell.Count;
            start = cell.Start; this.reverse = reverse;
            if (reverse) next = end - 1;
            Current = 0;
        }
        public MemberQuery GetEnumerator() => this;
        public int Current { get; private set; }
        public bool MoveNext()
        {
            if (reverse)
            {
                if (next < start) return false;
                Current = grid.snapshot[next--]; return true;
            }
            if (useSnapshot)
            {
                if (next == end) return false;
                Current = grid.snapshot[next++]; return true;
            }
            if (next == 0) return false;
            var member = grid.members[next - 1];
            Current = member.Entry; next = member.Next; return true;
        }
    }

    internal struct CellQuery
    {
        private readonly FlatGridLayout grid;
        private readonly int left, bottom, right, top;
        private readonly bool scanOccupied;
        private int cursor, x, y;
        public int RemainingWork { get; private set; }
        public bool ConsumeWork()
        {
            if (RemainingWork == int.MaxValue) return true;
            if (RemainingWork <= 0) return false;
            RemainingWork--; return true;
        }
        internal CellQuery(FlatGridLayout grid, int left, int bottom, int right, int top, int work = int.MaxValue)
        {
            this.grid = grid;
            this.left = (int)Math.Clamp((long)left - grid.MinimumX, 0, grid.Width);
            this.bottom = (int)Math.Clamp((long)bottom - grid.MinimumY, 0, grid.Height);
            this.right = (int)Math.Clamp((long)right - grid.MinimumX, -1, grid.Width - 1);
            this.top = (int)Math.Clamp((long)top - grid.MinimumY, -1, grid.Height - 1);
            scanOccupied = (long)(this.right - this.left + 1) * (this.top - this.bottom + 1) > grid.CellCount;
            cursor = 0; x = this.left; y = this.bottom; Current = 0;
            RemainingWork = work;
        }
        public CellQuery GetEnumerator() => this;
        public int Current { get; private set; }
        public bool MoveNext()
        {
            if (left > right || bottom > top) return false;
            if (scanOccupied)
            {
                while (cursor < grid.CellCount)
                {
                    if (!ConsumeWork()) return false;
                    var cell = grid.occupied[cursor++];
                    var cellY = cell / grid.Width; var cellX = cell - cellY * grid.Width;
                    if (cellX < left || cellX > right || cellY < bottom || cellY > top) continue;
                    Current = cell; return true;
                }
                return false;
            }
            while (y <= top)
            {
                if (!ConsumeWork()) return false;
                var cell = x + y * grid.Width;
                if (++x > right) { x = left; y++; }
                if (grid.cells[cell].Count == 0) continue;
                Current = cell; return true;
            }
            return false;
        }
    }
}
