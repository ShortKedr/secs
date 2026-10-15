using Godot;
using SKECS;

public partial class Smoke : Node
{
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

    public override void _Ready()
    {
        using (var world = new World())
        {
            var entity = world.CreateEntity(new Position { X = 2 });
            var counter = new CountProcessor();
            world.Query<Position>().ForEach(ref counter);
            if (counter.Count != 1 || world.Get<Position>(entity).X != 3)
            {
                GD.PushError("SKECS Godot consumer smoke failed.");
                GetTree().Quit(1);
                return;
            }
        }

        GD.Print("SKECS_GODOT_OK");
        GetTree().Quit(0);
    }
}
