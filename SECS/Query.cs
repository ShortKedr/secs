using System.Collections.Generic;

namespace SECS;

public readonly unsafe struct Query<T1>
    where T1 : unmanaged
{
    private readonly QueryCacheEntry _entry;
    private readonly int _component1Id;

    internal Query(World world, QueryCacheEntry entry)
    {
        _entry = entry;
        _component1Id = world.GetComponentType<T1>().Id;
    }

    public void ForEach<TProcessor>(ref TProcessor processor)
        where TProcessor : struct, IQueryProcessor<T1>
    {
        var matches = _entry.Matches;
        for (var i = 0; i < matches.Count; i++)
        {
            var chunks = matches[i].Chunks;
            for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                var pointer = (T1*)chunk.GetComponentPointer(_component1Id);
                var entities = chunk.Entities;
                var versions = chunk.Versions;
                for (var row = 0; row < chunk.Count; row++)
                {
                    processor.Execute(new Entity(entities[row], versions[row]), ref pointer[row]);
                }
            }
        }
    }

    public QueryChunkEnumerable<T1> Chunks() => new(_entry, _component1Id);
}

public readonly unsafe struct Query<T1, T2>
    where T1 : unmanaged
    where T2 : unmanaged
{
    private readonly QueryCacheEntry _entry;
    private readonly int _component1Id;
    private readonly int _component2Id;

    internal Query(World world, QueryCacheEntry entry)
    {
        _entry = entry;
        _component1Id = world.GetComponentType<T1>().Id;
        _component2Id = world.GetComponentType<T2>().Id;
    }

    public void ForEach<TProcessor>(ref TProcessor processor)
        where TProcessor : struct, IQueryProcessor<T1, T2>
    {
        var matches = _entry.Matches;
        for (var i = 0; i < matches.Count; i++)
        {
            var chunks = matches[i].Chunks;
            for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                var pointer1 = (T1*)chunk.GetComponentPointer(_component1Id);
                var pointer2 = (T2*)chunk.GetComponentPointer(_component2Id);
                var entities = chunk.Entities;
                var versions = chunk.Versions;
                for (var row = 0; row < chunk.Count; row++)
                {
                    processor.Execute(new Entity(entities[row], versions[row]), ref pointer1[row], ref pointer2[row]);
                }
            }
        }
    }

    public QueryChunkEnumerable<T1, T2> Chunks() => new(_entry, _component1Id, _component2Id);
}

public readonly unsafe struct Query<T1, T2, T3>
    where T1 : unmanaged
    where T2 : unmanaged
    where T3 : unmanaged
{
    private readonly QueryCacheEntry _entry;
    private readonly int _component1Id;
    private readonly int _component2Id;
    private readonly int _component3Id;

    internal Query(World world, QueryCacheEntry entry)
    {
        _entry = entry;
        _component1Id = world.GetComponentType<T1>().Id;
        _component2Id = world.GetComponentType<T2>().Id;
        _component3Id = world.GetComponentType<T3>().Id;
    }

    public void ForEach<TProcessor>(ref TProcessor processor)
        where TProcessor : struct, IQueryProcessor<T1, T2, T3>
    {
        var matches = _entry.Matches;
        for (var i = 0; i < matches.Count; i++)
        {
            var chunks = matches[i].Chunks;
            for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                var pointer1 = (T1*)chunk.GetComponentPointer(_component1Id);
                var pointer2 = (T2*)chunk.GetComponentPointer(_component2Id);
                var pointer3 = (T3*)chunk.GetComponentPointer(_component3Id);
                var entities = chunk.Entities;
                var versions = chunk.Versions;
                for (var row = 0; row < chunk.Count; row++)
                {
                    processor.Execute(new Entity(entities[row], versions[row]), ref pointer1[row], ref pointer2[row], ref pointer3[row]);
                }
            }
        }
    }

    public QueryChunkEnumerable<T1, T2, T3> Chunks() => new(_entry, _component1Id, _component2Id, _component3Id);
}

public readonly unsafe struct QueryChunkEnumerable
{
    private readonly QueryCacheEntry _entry;

    internal QueryChunkEnumerable(QueryCacheEntry entry)
    {
        _entry = entry;
    }

    public QueryChunkEnumerator GetEnumerator() => new(_entry);
}

public unsafe struct QueryChunkEnumerator
{
    private readonly List<Archetype> _matches;
    private int _archetypeIndex;
    private int _chunkIndex;

    internal QueryChunkEnumerator(QueryCacheEntry entry)
    {
        _matches = entry.Matches;
        _archetypeIndex = 0;
        _chunkIndex = -1;
        Current = default;
    }

    public RawChunkView Current { get; private set; }

    public bool MoveNext()
    {
        while (_archetypeIndex < _matches.Count)
        {
            var chunks = _matches[_archetypeIndex].Chunks;
            _chunkIndex++;
            if (_chunkIndex < chunks.Count)
            {
                Current = chunks[_chunkIndex].AsRawView();
                return true;
            }

            _archetypeIndex++;
            _chunkIndex = -1;
        }

        return false;
    }
}

public readonly unsafe struct QueryChunkEnumerable<T1>
    where T1 : unmanaged
{
    private readonly QueryCacheEntry _entry;
    private readonly int _component1Id;

    internal QueryChunkEnumerable(QueryCacheEntry entry, int component1Id)
    {
        _entry = entry;
        _component1Id = component1Id;
    }

    public QueryChunkEnumerator<T1> GetEnumerator() => new(_entry, _component1Id);
}

public unsafe struct QueryChunkEnumerator<T1>
    where T1 : unmanaged
{
    private readonly List<Archetype> _matches;
    private readonly int _component1Id;
    private int _archetypeIndex;
    private int _chunkIndex;

    internal QueryChunkEnumerator(QueryCacheEntry entry, int component1Id)
    {
        _matches = entry.Matches;
        _component1Id = component1Id;
        _archetypeIndex = 0;
        _chunkIndex = -1;
        Current = default;
    }

    public QueryChunkView<T1> Current { get; private set; }

    public bool MoveNext()
    {
        while (_archetypeIndex < _matches.Count)
        {
            var chunks = _matches[_archetypeIndex].Chunks;
            _chunkIndex++;
            if (_chunkIndex < chunks.Count)
            {
                Current = chunks[_chunkIndex].AsView<T1>(_component1Id);
                return true;
            }

            _archetypeIndex++;
            _chunkIndex = -1;
        }

        return false;
    }
}

public readonly unsafe struct QueryChunkEnumerable<T1, T2>
    where T1 : unmanaged
    where T2 : unmanaged
{
    private readonly QueryCacheEntry _entry;
    private readonly int _component1Id;
    private readonly int _component2Id;

    internal QueryChunkEnumerable(QueryCacheEntry entry, int component1Id, int component2Id)
    {
        _entry = entry;
        _component1Id = component1Id;
        _component2Id = component2Id;
    }

    public QueryChunkEnumerator<T1, T2> GetEnumerator() => new(_entry, _component1Id, _component2Id);
}

public unsafe struct QueryChunkEnumerator<T1, T2>
    where T1 : unmanaged
    where T2 : unmanaged
{
    private readonly List<Archetype> _matches;
    private readonly int _component1Id;
    private readonly int _component2Id;
    private int _archetypeIndex;
    private int _chunkIndex;

    internal QueryChunkEnumerator(QueryCacheEntry entry, int component1Id, int component2Id)
    {
        _matches = entry.Matches;
        _component1Id = component1Id;
        _component2Id = component2Id;
        _archetypeIndex = 0;
        _chunkIndex = -1;
        Current = default;
    }

    public QueryChunkView<T1, T2> Current { get; private set; }

    public bool MoveNext()
    {
        while (_archetypeIndex < _matches.Count)
        {
            var chunks = _matches[_archetypeIndex].Chunks;
            _chunkIndex++;
            if (_chunkIndex < chunks.Count)
            {
                Current = chunks[_chunkIndex].AsView<T1, T2>(_component1Id, _component2Id);
                return true;
            }

            _archetypeIndex++;
            _chunkIndex = -1;
        }

        return false;
    }
}

public readonly unsafe struct QueryChunkEnumerable<T1, T2, T3>
    where T1 : unmanaged
    where T2 : unmanaged
    where T3 : unmanaged
{
    private readonly QueryCacheEntry _entry;
    private readonly int _component1Id;
    private readonly int _component2Id;
    private readonly int _component3Id;

    internal QueryChunkEnumerable(QueryCacheEntry entry, int component1Id, int component2Id, int component3Id)
    {
        _entry = entry;
        _component1Id = component1Id;
        _component2Id = component2Id;
        _component3Id = component3Id;
    }

    public QueryChunkEnumerator<T1, T2, T3> GetEnumerator() => new(_entry, _component1Id, _component2Id, _component3Id);
}

public unsafe struct QueryChunkEnumerator<T1, T2, T3>
    where T1 : unmanaged
    where T2 : unmanaged
    where T3 : unmanaged
{
    private readonly List<Archetype> _matches;
    private readonly int _component1Id;
    private readonly int _component2Id;
    private readonly int _component3Id;
    private int _archetypeIndex;
    private int _chunkIndex;

    internal QueryChunkEnumerator(QueryCacheEntry entry, int component1Id, int component2Id, int component3Id)
    {
        _matches = entry.Matches;
        _component1Id = component1Id;
        _component2Id = component2Id;
        _component3Id = component3Id;
        _archetypeIndex = 0;
        _chunkIndex = -1;
        Current = default;
    }

    public QueryChunkView<T1, T2, T3> Current { get; private set; }

    public bool MoveNext()
    {
        while (_archetypeIndex < _matches.Count)
        {
            var chunks = _matches[_archetypeIndex].Chunks;
            _chunkIndex++;
            if (_chunkIndex < chunks.Count)
            {
                Current = chunks[_chunkIndex].AsView<T1, T2, T3>(_component1Id, _component2Id, _component3Id);
                return true;
            }

            _archetypeIndex++;
            _chunkIndex = -1;
        }

        return false;
    }
}

public readonly unsafe struct QueryChunkView<T1>
    where T1 : unmanaged
{
    internal QueryChunkView(int count, int* entities, int* versions, T1* component1)
    {
        Count = count;
        Entities = entities;
        Versions = versions;
        Component1 = component1;
    }

    public int Count { get; }
    public int* Entities { get; }
    public int* Versions { get; }
    public T1* Component1 { get; }
}

public readonly unsafe struct QueryChunkView<T1, T2>
    where T1 : unmanaged
    where T2 : unmanaged
{
    internal QueryChunkView(int count, int* entities, int* versions, T1* component1, T2* component2)
    {
        Count = count;
        Entities = entities;
        Versions = versions;
        Component1 = component1;
        Component2 = component2;
    }

    public int Count { get; }
    public int* Entities { get; }
    public int* Versions { get; }
    public T1* Component1 { get; }
    public T2* Component2 { get; }
}

public readonly unsafe struct QueryChunkView<T1, T2, T3>
    where T1 : unmanaged
    where T2 : unmanaged
    where T3 : unmanaged
{
    internal QueryChunkView(int count, int* entities, int* versions, T1* component1, T2* component2, T3* component3)
    {
        Count = count;
        Entities = entities;
        Versions = versions;
        Component1 = component1;
        Component2 = component2;
        Component3 = component3;
    }

    public int Count { get; }
    public int* Entities { get; }
    public int* Versions { get; }
    public T1* Component1 { get; }
    public T2* Component2 { get; }
    public T3* Component3 { get; }
}

public readonly unsafe struct RawChunkView
{
    internal RawChunkView(int count, int* entities, int* versions)
    {
        Count = count;
        Entities = entities;
        Versions = versions;
    }

    public int Count { get; }
    public int* Entities { get; }
    public int* Versions { get; }
}
