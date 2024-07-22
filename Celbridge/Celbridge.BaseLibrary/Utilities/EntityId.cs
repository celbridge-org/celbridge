namespace Celbridge.Utilities;

/// <summary>
/// A unique numeric identifier for any type of entity.
/// </summary>
public readonly struct EntityId : IComparable<EntityId>
{
    /// Monoticially increasing integer.
    private static ulong _nextId = 0;

    public ulong Id { get; }

    private EntityId(ulong id)
    {
        Id = id;
    }

    /// <summary>
    /// Factory method to create a new entity id.
    /// Each call to Create will return a new unique id.
    /// </summary>
    public static EntityId Create()
    {
        // Thread safe increment
        ulong newId = Interlocked.Increment(ref _nextId);
        return new EntityId(newId);
    }

    public int CompareTo(EntityId other)
    {
        return Id.CompareTo(other.Id);
    }

    public static bool operator ==(EntityId lhs, EntityId rhs)
    {
        return lhs.Equals(rhs);
    }

    public static bool operator !=(EntityId lhs, EntityId rhs)
    {
        return !lhs.Equals(rhs);
    }

    public override bool Equals(object? obj)
    {
        if (obj is EntityId other)
        {
            return Equals(other);
        }
        return false;
    }

    public bool Equals(EntityId other)
    {
        return Id == other.Id;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    public override string ToString()
    {
        return Id.ToString();
    }
}
