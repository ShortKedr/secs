namespace SKECS;

public sealed class QueryBuilder
{
    private readonly World _world;
    private ComponentSetKey _include = ComponentSetKey.Empty;
    private ComponentSetKey _exclude = ComponentSetKey.Empty;

    internal QueryBuilder(World world)
    {
        _world = world;
    }

    public QueryBuilder With<T>()
        where T : unmanaged
    {
        _include = _include.Add(_world.GetComponentType<T>().Id);
        return this;
    }

    public QueryBuilder Without<T>()
        where T : unmanaged
    {
        _exclude = _exclude.Add(_world.GetComponentType<T>().Id);
        return this;
    }

    public QueryDescription Build()
    {
        return new QueryDescription(_world.GetOrCreateQueryCache(new QueryCacheKey(_include, _exclude)));
    }
}

public readonly struct QueryDescription
{
    private readonly QueryCacheEntry _entry;

    internal QueryDescription(QueryCacheEntry entry)
    {
        _entry = entry;
    }

    public int MatchCount => _entry.Matches.Count;

    public QueryChunkEnumerable Chunks() => new(_entry);
}
