using System.Runtime.InteropServices;

namespace SKECS.Benchmarks;

public struct StressPosition { public int X, Y, Z; }
public struct StressVelocity { public int X, Y, Z; }
public struct StressHot { public int Value; }
public struct StressTag1 { public int Value; }
public struct StressTag2 { public int Value; }
public struct StressTag3 { public int Value; }
public struct StressTag4 { public int Value; }
public struct StressTag5 { public int Value; }
public struct StressTag6 { public int Value; }
public struct StressTag7 { public int Value; }
public struct StressTag8 { public int Value; }

[StructLayout(LayoutKind.Explicit, Size = 256)]
public struct StressPayload
{
    [FieldOffset(0)] public long Head;
    [FieldOffset(248)] public long Tail;
}

public readonly record struct StressSnapshot(int X, int Y, int Z, bool Alive,
    bool Hot, int Shape, bool HasPayload, long Head, long Tail);

// Batch-level contract: dispatch occurs once per pass, not once per entity.
public interface IStressBackend : IDisposable
{
    void MoveAll(bool hotOnly, bool wide);
    long ReadRandom(int[] indices);
    void AddPayload(int[] indices);
    void RemovePayload(int[] indices);
    void Recycle(int[] indices);
    StressSnapshot Read(int logicalIndex);
    int Count(bool hotOnly);
    void ValidateRetired();
}

public static class StressData
{
    public const int LayoutCount = 256;
    public const int HotStride = 100;
    public const long TailSalt = 0x5A5A5A5A;
    public static int Shape(int index) => index & (LayoutCount - 1);
    public static StressPosition Position(int index) => new() { X = index, Y = -index, Z = index ^ 17 };
    public static StressVelocity Velocity => new() { X = 1, Y = -1, Z = 2 };
    public static StressPayload Payload(int index) => new() { Head = index, Tail = index ^ TailSalt };
}
