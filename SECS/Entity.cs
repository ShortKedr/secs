namespace SECS;

public readonly struct Entity : System.IEquatable<Entity>
{
    public Entity(int id, int version)
    {
        Id = id;
        Version = version;
    }

    public int Id { get; }

    public int Version { get; }

    public bool IsValid => Id >= 0;

    public bool Equals(Entity other) => Id == other.Id && Version == other.Version;

    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    public override int GetHashCode() => System.HashCode.Combine(Id, Version);

    public override string ToString() => $"Entity({Id}:{Version})";

    public static bool operator ==(Entity left, Entity right) => left.Equals(right);

    public static bool operator !=(Entity left, Entity right) => !left.Equals(right);
}
