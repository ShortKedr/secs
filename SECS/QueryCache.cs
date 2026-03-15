using System.Collections.Generic;

namespace SECS;

internal readonly struct QueryCacheKey
{
    public QueryCacheKey(ComponentSetKey include, ComponentSetKey exclude)
    {
        Include = include;
        Exclude = exclude;
    }

    public ComponentSetKey Include { get; }

    public ComponentSetKey Exclude { get; }

    public bool Matches(ComponentSetKey candidate)
    {
        return candidate.ContainsAll(Include) && !candidate.ContainsAny(Exclude);
    }
}

internal sealed class QueryCacheEntry
{
    public QueryCacheEntry(QueryCacheKey key)
    {
        Key = key;
    }

    public QueryCacheKey Key { get; }

    public List<Archetype> Matches { get; } = new();
}
