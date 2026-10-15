namespace SKECS;

public interface IQueryProcessor<T1>
    where T1 : unmanaged
{
    void Execute(Entity entity, ref T1 component1);
}

public interface IQueryProcessor<T1, T2>
    where T1 : unmanaged
    where T2 : unmanaged
{
    void Execute(Entity entity, ref T1 component1, ref T2 component2);
}

public interface IQueryProcessor<T1, T2, T3>
    where T1 : unmanaged
    where T2 : unmanaged
    where T3 : unmanaged
{
    void Execute(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3);
}
