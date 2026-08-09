namespace Celbridge.Core;

/// <summary>
/// A unique numeric identifier for a command.
/// </summary>
public readonly struct CommandId : IComparable<CommandId>
{
    /// Monoticially increasing integer.
    private static ulong _nextId = 0;

    private readonly ulong _id = 0; // Default to invalid id of 0

    public ulong Id => _id;

    public CommandId(ulong id)
    {
        _id = id;
    }

    /// <summary>
    /// Factory method to create a new command id.
    /// Each call to Create will return a new unique id.
    /// </summary>
    public static CommandId Create()
    {
        // Thread safe increment
        ulong newId = Interlocked.Increment(ref _nextId);
        return new CommandId(newId);
    }

    /// <summary>
    /// An invalid command id has a value of 0.
    /// </summary>
    public static CommandId InvalidId { get; } = new CommandId(0);

    /// <summary>
    /// Returns true if the command id is valid.
    /// </summary>
    public bool IsValid => Id != InvalidId.Id;

    public int CompareTo(CommandId other)
    {
        return Id.CompareTo(other.Id);
    }

    public static bool operator ==(CommandId lhs, CommandId rhs)
    {
        return lhs.Equals(rhs);
    }

    public static bool operator !=(CommandId lhs, CommandId rhs)
    {
        return !lhs.Equals(rhs);
    }

    public override bool Equals(object? obj)
    {
        if (obj is CommandId other)
        {
            return Equals(other);
        }
        return false;
    }

    public bool Equals(CommandId other)
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
