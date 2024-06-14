namespace Celbridge.BaseLibrary.Commands;

/// <summary>
/// A unique identifier for commands.
/// </summary>
public readonly struct CommandId : IComparable<CommandId>
{
    /// Monoticially increasing integer.
    private static ulong _nextId = 0;

    public ulong Id { get; }

    private CommandId(ulong id)
    {
        Id = id;
    }

    /// <summary>
    /// Factory method to create a new command id.
    /// </summary>
    public static CommandId Create()
    {
        // Thread safe increment
        ulong newId = Interlocked.Increment(ref _nextId);
        return new CommandId(newId);
    }

    public int CompareTo(CommandId other)
    {
        return Id.CompareTo(other.Id);
    }

    // Override the equality operator
    public static bool operator ==(CommandId lhs, CommandId rhs)
    {
        return lhs.Equals(rhs);
    }

    // Override the inequality operator
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

    // Implement the IEquatable<MyStruct> interface
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
