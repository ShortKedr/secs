using SKECS;
using UnityEngine;

public sealed class SKECSRuntimeSmoke : MonoBehaviour
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

    public static void Check()
    {
        using (var world = new World())
        {
            var entity = world.CreateEntity(new Position { X = 2 });
            var counter = new CountProcessor();
            world.Query<Position>().ForEach(ref counter);
            if (counter.Count != 1 || world.Get<Position>(entity).X != 3)
                throw new System.Exception("SKECS Unity consumer smoke failed.");
        }
    }

    private void Awake()
    {
        try
        {
            Check();
            Debug.Log("SKECS_UNITY_OK");
            Application.Quit(0);
        }
        catch (System.Exception error)
        {
            Debug.LogException(error);
            Application.Quit(1);
        }
    }
}
