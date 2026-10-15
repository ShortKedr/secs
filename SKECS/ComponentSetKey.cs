using System;
using System.Collections.Generic;

namespace SKECS;

internal readonly struct ComponentSetKey : IEquatable<ComponentSetKey>
{
    public static readonly ComponentSetKey Empty = new(Array.Empty<int>());

    public ComponentSetKey(int[] typeIds)
    {
        TypeIds = typeIds;
        HashCode = ComputeHash(typeIds);
    }

    public int[] TypeIds { get; }

    public int HashCode { get; }

    public bool Contains(int typeId) => Array.BinarySearch(TypeIds, typeId) >= 0;

    public ComponentSetKey Add(int typeId)
    {
        var index = Array.BinarySearch(TypeIds, typeId);
        if (index >= 0)
        {
            return this;
        }

        index = ~index;
        var next = new int[TypeIds.Length + 1];
        if (index > 0)
        {
            Array.Copy(TypeIds, 0, next, 0, index);
        }

        next[index] = typeId;
        if (index < TypeIds.Length)
        {
            Array.Copy(TypeIds, index, next, index + 1, TypeIds.Length - index);
        }

        return new ComponentSetKey(next);
    }

    public ComponentSetKey Remove(int typeId)
    {
        var index = Array.BinarySearch(TypeIds, typeId);
        if (index < 0)
        {
            return this;
        }

        if (TypeIds.Length == 1)
        {
            return Empty;
        }

        var next = new int[TypeIds.Length - 1];
        if (index > 0)
        {
            Array.Copy(TypeIds, 0, next, 0, index);
        }

        if (index < next.Length)
        {
            Array.Copy(TypeIds, index + 1, next, index, next.Length - index);
        }

        return new ComponentSetKey(next);
    }

    public bool ContainsAll(ComponentSetKey other)
    {
        if (other.TypeIds.Length == 0)
        {
            return true;
        }

        var i = 0;
        var j = 0;
        while (i < TypeIds.Length && j < other.TypeIds.Length)
        {
            var left = TypeIds[i];
            var right = other.TypeIds[j];
            if (left == right)
            {
                i++;
                j++;
                continue;
            }

            if (left < right)
            {
                i++;
                continue;
            }

            return false;
        }

        return j == other.TypeIds.Length;
    }

    public bool ContainsAny(ComponentSetKey other)
    {
        var i = 0;
        var j = 0;
        while (i < TypeIds.Length && j < other.TypeIds.Length)
        {
            var left = TypeIds[i];
            var right = other.TypeIds[j];
            if (left == right)
            {
                return true;
            }

            if (left < right)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return false;
    }

    public bool Equals(ComponentSetKey other)
    {
        if (HashCode != other.HashCode || TypeIds.Length != other.TypeIds.Length)
        {
            return false;
        }

        for (var i = 0; i < TypeIds.Length; i++)
        {
            if (TypeIds[i] != other.TypeIds[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is ComponentSetKey other && Equals(other);

    public override int GetHashCode() => HashCode;

    public static unsafe ComponentSetKey Create(int* source, int count)
    {
        if (count == 0)
        {
            return Empty;
        }

        var values = new int[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = source[i];
        }

        Array.Sort(values);
        return new ComponentSetKey(Distinct(values));
    }

    private static int[] Distinct(int[] source)
    {
        if (source.Length <= 1)
        {
            return source;
        }

        var count = 1;
        for (var i = 1; i < source.Length; i++)
        {
            if (source[i] != source[count - 1])
            {
                source[count++] = source[i];
            }
        }

        if (count == source.Length)
        {
            return source;
        }

        var result = new int[count];
        Array.Copy(source, result, count);
        return result;
    }

    private static int ComputeHash(IReadOnlyList<int> ids)
    {
        unchecked
        {
            var hash = 17;
            for (var i = 0; i < ids.Count; i++)
            {
                hash = (hash * 31) + ids[i];
            }

            return hash;
        }
    }
}
