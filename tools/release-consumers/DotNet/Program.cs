using System;
using SECS;

internal static class Program
{
    private static int Main()
    {
        using (var world = new World())
        {
            var entity = world.CreateEntity(new Position { X = 2 });
            var counter = new CountProcessor();
            world.Query<Position>().ForEach(ref counter);

            if (counter.Count != 1 || world.Get<Position>(entity).X != 3)
            {
                Console.Error.WriteLine("SECS consumer smoke failed: query/Get disagreed.");
                return 1;
            }
        }

        Console.WriteLine("SECS_CONSUMER_OK");
        return 0;
    }

    private struct Position
    {
        public int X;
    }

    private struct CountProcessor : IQueryProcessor<Position>
    {
        public int Count;

        public void Execute(Entity entity, ref Position position)
        {
            Count++;
            position.X++;
        }
    }
}
