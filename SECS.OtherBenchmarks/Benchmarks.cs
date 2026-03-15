using BenchmarkDotNet.Attributes;
using SECS;

namespace SECS.OtherBenchmarks;

[MemoryDiagnoser] public class CreateEmptyEntity : CreateEntityBenchmarkBase { protected override ComponentRecipe[] Recipes => [BenchmarkRecipes.Empty]; }
[MemoryDiagnoser] public class CreateEntityWith1Component : CreateEntityBenchmarkBase { protected override ComponentRecipe[] Recipes => [BenchmarkRecipes.C1]; }
[MemoryDiagnoser] public class CreateEntityWith1RandomComponent : CreateEntityBenchmarkBase { protected override ComponentRecipe[] Recipes => BenchmarkRecipes.Random1; }
[MemoryDiagnoser] public class CreateEntityWith2Components : CreateEntityBenchmarkBase { protected override ComponentRecipe[] Recipes => [BenchmarkRecipes.C1C2]; }
[MemoryDiagnoser] public class CreateEntityWith2RandomComponents : CreateEntityBenchmarkBase { protected override ComponentRecipe[] Recipes => BenchmarkRecipes.Random2; }
[MemoryDiagnoser] public class CreateEntityWith3Components : CreateEntityBenchmarkBase { protected override ComponentRecipe[] Recipes => [BenchmarkRecipes.C1C2C3]; }
[MemoryDiagnoser] public class CreateEntityWith3RandomComponents : CreateEntityBenchmarkBase { protected override ComponentRecipe[] Recipes => BenchmarkRecipes.Random3; }
[MemoryDiagnoser] public class CreateEntityWith4Components : CreateEntityBenchmarkBase { protected override ComponentRecipe[] Recipes => [BenchmarkRecipes.C1C2C3C4]; }

[MemoryDiagnoser] public class Add1Component : AddComponentBenchmarkBase { protected override ComponentRecipe[] Recipes => [BenchmarkRecipes.C1]; }
[MemoryDiagnoser] public class Add1ComponentRandomOrder : AddComponentBenchmarkBase { protected override ComponentRecipe[] Recipes => [BenchmarkRecipes.C1]; protected override bool ShuffleBeforeRun => true; }
[MemoryDiagnoser] public class Add1RandomComponent : AddComponentBenchmarkBase { protected override ComponentRecipe[] Recipes => BenchmarkRecipes.Random1; }
[MemoryDiagnoser] public class Add1RandomComponentRandomOrder : AddComponentBenchmarkBase { protected override ComponentRecipe[] Recipes => BenchmarkRecipes.Random1; protected override bool ShuffleBeforeRun => true; }
[MemoryDiagnoser] public class Add2Components : AddComponentBenchmarkBase { protected override ComponentRecipe[] Recipes => [BenchmarkRecipes.C1C2]; }
[MemoryDiagnoser] public class Add2ComponentsRandomOrder : AddComponentBenchmarkBase { protected override ComponentRecipe[] Recipes => [BenchmarkRecipes.C1C2]; protected override bool ShuffleBeforeRun => true; }
[MemoryDiagnoser] public class Add2RandomComponents : AddComponentBenchmarkBase { protected override ComponentRecipe[] Recipes => BenchmarkRecipes.Random2; }
[MemoryDiagnoser] public class Add2RandomComponentsRandomOrder : AddComponentBenchmarkBase { protected override ComponentRecipe[] Recipes => BenchmarkRecipes.Random2; protected override bool ShuffleBeforeRun => true; }
[MemoryDiagnoser] public class Add3Components : AddComponentBenchmarkBase { protected override ComponentRecipe[] Recipes => [BenchmarkRecipes.C1C2C3]; }
[MemoryDiagnoser] public class Add3ComponentsRandomOrder : AddComponentBenchmarkBase { protected override ComponentRecipe[] Recipes => [BenchmarkRecipes.C1C2C3]; protected override bool ShuffleBeforeRun => true; }
[MemoryDiagnoser] public class Add3RandomComponents : AddComponentBenchmarkBase { protected override ComponentRecipe[] Recipes => BenchmarkRecipes.Random3; }
[MemoryDiagnoser] public class Add3RandomComponentsRandomOrder : AddComponentBenchmarkBase { protected override ComponentRecipe[] Recipes => BenchmarkRecipes.Random3; protected override bool ShuffleBeforeRun => true; }
[MemoryDiagnoser] public class Add4Components : AddComponentBenchmarkBase { protected override ComponentRecipe[] Recipes => [BenchmarkRecipes.C1C2C3C4]; }
[MemoryDiagnoser] public class Add4ComponentsRandomOrder : AddComponentBenchmarkBase { protected override ComponentRecipe[] Recipes => [BenchmarkRecipes.C1C2C3C4]; protected override bool ShuffleBeforeRun => true; }

[MemoryDiagnoser] public class Remove1Component : RemoveComponentBenchmarkBase { protected override ComponentRecipe Recipe => BenchmarkRecipes.C1; }
[MemoryDiagnoser] public class Remove1ComponentRandomOrder : RemoveComponentBenchmarkBase { protected override ComponentRecipe Recipe => BenchmarkRecipes.C1; protected override bool ShuffleBeforeRun => true; }
[MemoryDiagnoser] public class Remove2Components : RemoveComponentBenchmarkBase { protected override ComponentRecipe Recipe => BenchmarkRecipes.C1C2; }
[MemoryDiagnoser] public class Remove2ComponentsRandomOrder : RemoveComponentBenchmarkBase { protected override ComponentRecipe Recipe => BenchmarkRecipes.C1C2; protected override bool ShuffleBeforeRun => true; }
[MemoryDiagnoser] public class Remove3Components : RemoveComponentBenchmarkBase { protected override ComponentRecipe Recipe => BenchmarkRecipes.C1C2C3; }
[MemoryDiagnoser] public class Remove3ComponentsRandomOrder : RemoveComponentBenchmarkBase { protected override ComponentRecipe Recipe => BenchmarkRecipes.C1C2C3; protected override bool ShuffleBeforeRun => true; }
[MemoryDiagnoser] public class Remove4Components : RemoveComponentBenchmarkBase { protected override ComponentRecipe Recipe => BenchmarkRecipes.C1C2C3C4; }
[MemoryDiagnoser] public class Remove4ComponentsRandomOrder : RemoveComponentBenchmarkBase { protected override ComponentRecipe Recipe => BenchmarkRecipes.C1C2C3C4; protected override bool ShuffleBeforeRun => true; }

[MemoryDiagnoser] public class OneAddOneComponent : StructuralChangeBenchmarkBase { protected override ComponentRecipe InitialRecipe => BenchmarkRecipes.C1; protected override ComponentRecipe DeltaRecipe => BenchmarkRecipes.C2; protected override bool IsAddOperation => true; }
[MemoryDiagnoser] public class OneAddTwoComponents : StructuralChangeBenchmarkBase { protected override ComponentRecipe InitialRecipe => BenchmarkRecipes.C1; protected override ComponentRecipe DeltaRecipe => new(2, 3); protected override bool IsAddOperation => true; }
[MemoryDiagnoser] public class OneAddThreeComponents : StructuralChangeBenchmarkBase { protected override ComponentRecipe InitialRecipe => BenchmarkRecipes.C1; protected override ComponentRecipe DeltaRecipe => new(2, 3, 4); protected override bool IsAddOperation => true; }
[MemoryDiagnoser] public class TwoAddOneComponent : StructuralChangeBenchmarkBase { protected override ComponentRecipe InitialRecipe => BenchmarkRecipes.C1C2; protected override ComponentRecipe DeltaRecipe => BenchmarkRecipes.C3; protected override bool IsAddOperation => true; }
[MemoryDiagnoser] public class TwoAddTwoComponents : StructuralChangeBenchmarkBase { protected override ComponentRecipe InitialRecipe => BenchmarkRecipes.C1C2; protected override ComponentRecipe DeltaRecipe => new(3, 4); protected override bool IsAddOperation => true; }
[MemoryDiagnoser] public class ThreeAddOneComponent : StructuralChangeBenchmarkBase { protected override ComponentRecipe InitialRecipe => BenchmarkRecipes.C1C2C3; protected override ComponentRecipe DeltaRecipe => BenchmarkRecipes.C4; protected override bool IsAddOperation => true; }
[MemoryDiagnoser] public class TwoRemoveOneComponent : StructuralChangeBenchmarkBase { protected override ComponentRecipe InitialRecipe => BenchmarkRecipes.C1C2; protected override ComponentRecipe DeltaRecipe => BenchmarkRecipes.C2; protected override bool IsAddOperation => false; }
[MemoryDiagnoser] public class ThreeRemoveOneComponent : StructuralChangeBenchmarkBase { protected override ComponentRecipe InitialRecipe => BenchmarkRecipes.C1C2C3; protected override ComponentRecipe DeltaRecipe => BenchmarkRecipes.C3; protected override bool IsAddOperation => false; }
[MemoryDiagnoser] public class ThreeRemoveTwoComponents : StructuralChangeBenchmarkBase { protected override ComponentRecipe InitialRecipe => BenchmarkRecipes.C1C2C3; protected override ComponentRecipe DeltaRecipe => new(2, 3); protected override bool IsAddOperation => false; }
[MemoryDiagnoser] public class FourRemoveOneComponent : StructuralChangeBenchmarkBase { protected override ComponentRecipe InitialRecipe => BenchmarkRecipes.C1C2C3C4; protected override ComponentRecipe DeltaRecipe => BenchmarkRecipes.C4; protected override bool IsAddOperation => false; }
[MemoryDiagnoser] public class FourRemoveTwoComponents : StructuralChangeBenchmarkBase { protected override ComponentRecipe InitialRecipe => BenchmarkRecipes.C1C2C3C4; protected override ComponentRecipe DeltaRecipe => new(3, 4); protected override bool IsAddOperation => false; }
[MemoryDiagnoser] public class FourRemoveThreeComponents : StructuralChangeBenchmarkBase { protected override ComponentRecipe InitialRecipe => BenchmarkRecipes.C1C2C3C4; protected override ComponentRecipe DeltaRecipe => new(2, 3, 4); protected override bool IsAddOperation => false; }

[MemoryDiagnoser]
public class SystemWith1Component : SystemBenchmarkBase
{
    protected override void ConfigureSystems()
    {
        var query = Context.World.Query<Component1>();
        Context.World.AddSystem(SystemStage.Update, _ =>
        {
            var processor = new IncrementSingleProcessor<Component1>();
            query.ForEach(ref processor);
        });
    }

    protected override void SeedEntities()
    {
        var slot = Context.PrepareSet(1);
        var i = 0;
        while (i < EntityCount)
        {
            Context.CreateEntities(slot, BenchmarkRecipes.C1);
            Context.World.Get<Component1>(slot[0]).Value = 0;
            i++;

            for (var j = 0; j < Padding && i < EntityCount; j++)
            {
                var entity = Context.World.CreateEntity();
                Context.World.Add(entity, default(Padding1));
                i++;
            }
        }
    }
}

[MemoryDiagnoser]
public class SystemWith2Components : SystemBenchmarkBase
{
    protected override void ConfigureSystems()
    {
        var query = Context.World.Query<Component1, Component2>();
        Context.World.AddSystem(SystemStage.Update, _ =>
        {
            var processor = new AddPairProcessor<Component1, Component2>();
            query.ForEach(ref processor);
        });
    }

    protected override void SeedEntities()
    {
        var slot = Context.PrepareSet(1);
        var i = 0;
        while (i < EntityCount)
        {
            Context.CreateEntities(slot, BenchmarkRecipes.C1C2);
            Context.World.Get<Component2>(slot[0]).Value = 1;
            i++;

            for (var j = 0; j < Padding && i < EntityCount; j++)
            {
                var entity = Context.World.CreateEntity();
                if ((j & 1) == 0)
                {
                    Context.World.Add(entity, default(Component1));
                }
                else
                {
                    Context.World.Add(entity, default(Component2));
                }

                i++;
            }
        }
    }
}

[MemoryDiagnoser]
public class SystemWith3Components : SystemBenchmarkBase
{
    protected override void ConfigureSystems()
    {
        var query = Context.World.Query<Component1, Component2, Component3>();
        Context.World.AddSystem(SystemStage.Update, _ =>
        {
            var processor = new AddTripleProcessor<Component1, Component2, Component3>();
            query.ForEach(ref processor);
        });
    }

    protected override void SeedEntities()
    {
        var slot = Context.PrepareSet(1);
        var i = 0;
        while (i < EntityCount)
        {
            Context.CreateEntities(slot, BenchmarkRecipes.C1C2C3);
            Context.World.Get<Component2>(slot[0]).Value = 1;
            Context.World.Get<Component3>(slot[0]).Value = 1;
            i++;

            for (var j = 0; j < Padding && i < EntityCount; j++)
            {
                var entity = Context.World.CreateEntity();
                switch (j % 3)
                {
                    case 0:
                        Context.World.Add(entity, default(Component1));
                        break;
                    case 1:
                        Context.World.Add(entity, default(Component2));
                        break;
                    default:
                        Context.World.Add(entity, default(Component3));
                        break;
                }

                i++;
            }
        }
    }
}
