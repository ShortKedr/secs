using System;

namespace SKECS;

/// <summary>A world-bound creation template which places entities directly in their final archetype.</summary>
public readonly struct EntityFactory<T1, T2> where T1 : unmanaged where T2 : unmanaged
{
    private readonly World _world;
    private readonly Archetype _archetype;
    private readonly int _type1;
    private readonly int _type2;

    internal EntityFactory(World world, Archetype archetype, int type1, int type2)
    { _world = world; _archetype = archetype; _type1 = type1; _type2 = type2; }

    public Entity Create(in T1 component1, in T2 component2)
        => _world.CreateEntityIn(_archetype, _type1, _type2, component1, component2);

    public void CreateBatch(Span<Entity> output, in T1 component1, in T2 component2)
    {
        for (var i = 0; i < output.Length; i++) output[i] = Create(component1, component2);
    }
}
