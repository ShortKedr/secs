using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace SECS;

internal sealed unsafe class ComponentTypeRegistry
{
    private readonly Dictionary<Type, ComponentType> _byRuntimeType = new();
    private readonly List<ComponentType> _byId = new();

    public ComponentType GetOrCreate<T>()
        where T : unmanaged
    {
        var runtimeType = typeof(T);
        if (_byRuntimeType.TryGetValue(runtimeType, out var type))
        {
            return type;
        }

        type = new ComponentType(
            _byId.Count,
            runtimeType,
            sizeof(T),
            GetAlignment<T>());
        _byRuntimeType.Add(runtimeType, type);
        _byId.Add(type);
        return type;
    }

    public ComponentType[] ResolveAll(int[] ids)
    {
        var result = new ComponentType[ids.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            result[i] = _byId[ids[i]];
        }

        return result;
    }

    private static int GetAlignment<T>()
        where T : unmanaged
    {
        return Math.Min(16, Math.Max(1, sizeof(T)));
    }
}

internal readonly struct ComponentType
{
    public ComponentType(int id, Type runtimeType, int size, int alignment)
    {
        Id = id;
        RuntimeType = runtimeType;
        Size = size;
        Alignment = alignment;
    }

    public int Id { get; }

    public Type RuntimeType { get; }

    public int Size { get; }

    public int Alignment { get; }
}
