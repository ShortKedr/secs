using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
namespace SKECS;

internal sealed unsafe class ComponentTypeRegistry
{
    private int[] _bySlot = CreateSlots(8);
    private readonly List<ComponentType> _byId = new();

    public ComponentType GetOrCreate<T>()
        where T : unmanaged
    {
        var slot = ComponentSlot<T>.Id;
        if ((uint)slot < (uint)_bySlot.Length && _bySlot[slot] >= 0)
        {
            return _byId[_bySlot[slot]];
        }

        if (slot >= _bySlot.Length)
        {
            var oldLength = _bySlot.Length;
            Array.Resize(ref _bySlot, Math.Max(slot + 1, oldLength * 2));
            for (var i = oldLength; i < _bySlot.Length; i++) _bySlot[i] = -1;
        }
        var type = new ComponentType(
            _byId.Count,
            typeof(T),
            sizeof(T),
            GetAlignment<T>());
        _bySlot[slot] = type.Id;
        _byId.Add(type);
        return type;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetId<T>() where T : unmanaged
    {
        var slot = ComponentSlot<T>.Id;
        return (uint)slot < (uint)_bySlot.Length && _bySlot[slot] >= 0 ? _bySlot[slot] : GetOrCreate<T>().Id;
    }

    private static int[] CreateSlots(int count)
    {
        var slots = new int[count];
        for (var i = 0; i < slots.Length; i++) slots[i] = -1;
        return slots;
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

internal static class ComponentSlots
{
    internal static int NextId;
}

internal static class ComponentSlot<T> where T : unmanaged
{
    internal static readonly int Id = Interlocked.Increment(ref ComponentSlots.NextId) - 1;
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
