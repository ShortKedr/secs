namespace SKECS.Benchmarks;

public sealed class StressWorkload : IDisposable
{
    public const int MixedFrameCount = 120;
    public static readonly string[] Scenarios =
    ["DenseIteration", "SparseIteration", "WideIteration", "RandomLookup", "PayloadChurn", "Recycle10Percent", "MixedFrames"];
    private readonly IStressBackend _backend;
    private readonly int[] _order, _churn, _read, _recycle;
    private readonly bool[] _mixedRecycled;
    private readonly int _count;
    public long Checksum { get; private set; }

    public StressWorkload(int count, Func<int, IStressBackend>? factory = null, bool wide = false)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        _count = count;
        _order = Enumerable.Range(0, count).ToArray();
        new Random(2052513957).Shuffle(_order);
        _churn = _order.Take(Math.Max(1, count / 20)).ToArray();
        _read = _order.Take(Math.Max(1, count / 10)).ToArray();
        _recycle = _order.Take(Math.Max(1, count / 100)).ToArray();
        _mixedRecycled = new bool[count];
        foreach (var i in _recycle) _mixedRecycled[i] = true;
        _backend = (factory ?? (n => new SkecsStressBackend(n)))(count);
        if (wide) _backend.AddPayload(_order);
    }

    public void Execute(string scenario)
    {
        switch (scenario)
        {
            case "DenseIteration": _backend.MoveAll(false, false); break;
            case "SparseIteration": _backend.MoveAll(true, false); break;
            case "WideIteration": _backend.MoveAll(false, true); break;
            case "RandomLookup": Checksum = _backend.ReadRandom(_order); break;
            case "PayloadChurn": _backend.AddPayload(_order); _backend.RemovePayload(_order); break;
            case "Recycle10Percent": _backend.Recycle(_read); break;
            case "MixedFrames":
                for (var frame = 0; frame < MixedFrameCount; frame++)
                {
                    _backend.MoveAll(false, false);
                    _backend.AddPayload(_churn);
                    _backend.RemovePayload(_churn);
                    Checksum = _backend.ReadRandom(_read);
                    _backend.Recycle(_recycle);
                }
                break;
            default: throw new ArgumentException("Unknown stress scenario", nameof(scenario));
        }
    }

    public void Validate(string scenario, int invocations)
    {
        if (_backend.Count(false) != _count || _backend.Count(true) != (_count + StressData.HotStride - 1) / StressData.HotStride)
            throw new InvalidOperationException("Stress filter count mismatch");
        long checksum = 0;
        for (var index = 0; index < _count; index++)
        {
            var hot = index % StressData.HotStride == 0;
            var moves = scenario switch
            {
                "DenseIteration" or "WideIteration" => invocations,
                "SparseIteration" => hot ? invocations : 0,
                "MixedFrames" => _mixedRecycled[index] ? 0 : MixedFrameCount * invocations,
                _ => 0
            };
            var actual = _backend.Read(index);
            var initial = StressData.Position(index);
            if (!actual.Alive || actual.Hot != hot || actual.Shape != StressData.Shape(index) ||
                actual.X != initial.X + moves || actual.Y != initial.Y - moves || actual.Z != initial.Z + moves * 2)
                throw new InvalidOperationException($"Stress entity mismatch at {index}: {scenario}");
            var wide = scenario == "WideIteration";
            if (actual.HasPayload != wide || (wide && (actual.Head != index + invocations || actual.Tail != (index ^ StressData.TailSalt) + invocations)))
                throw new InvalidOperationException($"Stress payload mismatch at {index}");
            checksum += (long)actual.X + actual.Y + actual.Z;
        }
        if (scenario == "RandomLookup" && invocations > 0 && checksum != Checksum)
            throw new InvalidOperationException("Random lookup checksum mismatch");
        _backend.ValidateRetired();
    }

    public void ValidatePayloadRoundTrip()
    {
        _backend.AddPayload(_order);
        foreach (var index in _order)
        {
            var snapshot = _backend.Read(index);
            if (!snapshot.HasPayload || snapshot.Head != index || snapshot.Tail != (index ^ StressData.TailSalt))
                throw new InvalidOperationException("Payload add failed");
        }
        _backend.RemovePayload(_order);
        Validate("PayloadChurn", 0);
    }
    public void Dispose() => _backend.Dispose();
}
