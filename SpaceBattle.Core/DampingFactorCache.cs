namespace SpaceBattle;

internal sealed class DampingFactorCache
{
    internal const int Capacity = 64;
    private readonly Entry[] entries = new Entry[Capacity];

    private struct Entry
    {
        public int DampingBits, DtBits;
        public float Factor;
        public bool Occupied;
    }

    public float Get(float damping, float dt)
    {
        // Re-evaluate nonfinite inputs, including NaN payloads, exactly as the scalar path does.
        if (!float.IsFinite(damping) || !float.IsFinite(dt)) return MathF.Exp(-damping * dt);
        var dampingBits = BitConverter.SingleToInt32Bits(damping);
        var dtBits = BitConverter.SingleToInt32Bits(dt);
        var hash = unchecked((uint)dampingBits * 0x9E3779B9u + (uint)dtBits);
        hash ^= hash >> 16;
        ref var entry = ref entries[(int)(hash & (Capacity - 1))];
        if (entry.Occupied && entry.DampingBits == dampingBits && entry.DtBits == dtBits)
            return entry.Factor;
        var factor = MathF.Exp(-damping * dt);
        // Collisions replace one slot; no entity IDs, serials, or unbounded history are retained.
        entry = new Entry { DampingBits = dampingBits, DtBits = dtBits, Factor = factor, Occupied = true };
        return factor;
    }
}
