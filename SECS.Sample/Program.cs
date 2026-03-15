using SECS;

using var world = new World(new WorldOptions(chunkCapacity: 128));

var entity = world.CreateEntity();
world.Add(entity, new Position { X = 0, Y = 0 });
world.Add(entity, new Velocity { X = 3, Y = 1 });

world.AddSystem(SystemStage.Update, static world =>
{
    var query = world.Query<Position, Velocity>();
    var processor = new MovementProcessor();
    query.ForEach(ref processor);
});

for (var tick = 0; tick < 5; tick++)
{
    world.Run(SystemStage.Update);
    ref var position = ref world.Get<Position>(entity);
    Console.WriteLine($"Tick {tick + 1}: ({position.X}, {position.Y})");
}

var moving = world.QueryBuilder().With<Position>().With<Velocity>().Build();
Console.WriteLine($"Matching archetypes: {moving.MatchCount}");

unsafe
{
    foreach (var chunk in world.Query<Position, Velocity>().Chunks())
    {
        Console.WriteLine($"Chunk rows: {chunk.Count}");
        for (var row = 0; row < chunk.Count; row++)
        {
            Console.WriteLine($"  Entity {chunk.Entities[row]} -> ({chunk.Component1[row].X}, {chunk.Component1[row].Y})");
        }
    }
}

internal struct Position
{
    public float X;
    public float Y;
}

internal struct Velocity
{
    public float X;
    public float Y;
}

internal struct MovementProcessor : IQueryProcessor<Position, Velocity>
{
    public void Execute(Entity entity, ref Position component1, ref Velocity component2)
    {
        _ = entity;
        component1.X += component2.X;
        component1.Y += component2.Y;
    }
}
