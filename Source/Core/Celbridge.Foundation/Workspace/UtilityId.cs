namespace Celbridge.Workspace;

/// <summary>
/// A strongly-typed identifier for a utility, built-in or contributed, in "{scope}.{name}" form (e.g.
/// "celbridge.explorer", "acme.notepad"). The same value addresses a utility on the rail, as a docked document,
/// and across the agent tools. A contributed utility's id equals its document editor id. Empty is the
/// "no utility" value.
/// </summary>
public readonly struct UtilityId : IEquatable<UtilityId>
{
    private readonly string? _id;

    private UtilityId(string id)
    {
        _id = id;
    }

    /// <summary>
    /// The "no utility" value.
    /// </summary>
    public static UtilityId Empty => new();

    /// <summary>
    /// True when this is the Empty value.
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(_id);

    /// <summary>
    /// Builds a utility id from a package name and document id, as "{packageName}.{documentId}". Built-in and
    /// contributed utilities share this form.
    /// </summary>
    public static UtilityId Create(string packageName, string documentId)
    {
        if (string.IsNullOrEmpty(packageName))
        {
            throw new ArgumentException("Package name must not be empty.", nameof(packageName));
        }
        if (string.IsNullOrEmpty(documentId))
        {
            throw new ArgumentException("Document id must not be empty.", nameof(documentId));
        }

        return new UtilityId($"{packageName}.{documentId}");
    }

    /// <summary>
    /// Parses a raw id string, such as a built-in id or an id received from an agent. Returns false for a null,
    /// empty, or whitespace string (matching is left to the caller, which reports an unknown id).
    /// </summary>
    public static bool TryCreate(string? id, out UtilityId utilityId)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            utilityId = Empty;
            return false;
        }

        utilityId = new UtilityId(id);
        return true;
    }

    public bool Equals(UtilityId other)
    {
        return string.Equals(_id, other._id, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is UtilityId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _id is null ? 0 : StringComparer.Ordinal.GetHashCode(_id);
    }

    public static bool operator ==(UtilityId left, UtilityId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(UtilityId left, UtilityId right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return _id ?? string.Empty;
    }
}
