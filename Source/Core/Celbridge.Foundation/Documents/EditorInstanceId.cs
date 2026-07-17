namespace Celbridge.Documents;

/// <summary>
/// A strongly-typed identifier for an editor instance, in lowercase kebab-case with optional dots
/// (e.g. "celbridge.markdown-document", "celbridge.explorer"). The same value addresses the instance's
/// editor factory, its rail button and dock location when it is a utility, and the agent tools.
/// Empty is the "no instance" value.
/// </summary>
public readonly struct EditorInstanceId : IEquatable<EditorInstanceId>
{
    private readonly string? _id;

    public EditorInstanceId(string id)
    {
        if (!IsValid(id))
        {
            throw new ArgumentException($"Invalid editor instance ID: '{id}'. Expected lowercase letters, digits, dots, and hyphens.", nameof(id));
        }

        _id = id;
    }

    /// <summary>
    /// The "no instance" value.
    /// </summary>
    public static EditorInstanceId Empty => new();

    /// <summary>
    /// True when this is the Empty value.
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(_id);

    /// <summary>
    /// Builds an instance id from a package name and contribution id, as "{packageName}.{contributionId}".
    /// </summary>
    public static EditorInstanceId Create(string packageName, string contributionId)
    {
        if (string.IsNullOrEmpty(packageName))
        {
            throw new ArgumentException("Package name must not be empty.", nameof(packageName));
        }
        if (string.IsNullOrEmpty(contributionId))
        {
            throw new ArgumentException("Contribution id must not be empty.", nameof(contributionId));
        }

        return new EditorInstanceId($"{packageName}.{contributionId}");
    }

    /// <summary>
    /// Returns true if the string is a valid editor instance ID: non-empty, using only lowercase
    /// letters, digits, dots, and hyphens.
    /// </summary>
    public static bool IsValid(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        foreach (var character in id)
        {
            if (!char.IsAsciiLetterLower(character) &&
                !char.IsAsciiDigit(character) &&
                character != '.' &&
                character != '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Tries to parse a string into an EditorInstanceId without throwing on invalid input.
    /// Returns true on success; false and EditorInstanceId.Empty on failure.
    /// </summary>
    public static bool TryParse(string? id, out EditorInstanceId result)
    {
        if (string.IsNullOrEmpty(id) ||
            !IsValid(id))
        {
            result = Empty;
            return false;
        }

        result = new EditorInstanceId(id);
        return true;
    }

    public override string ToString()
    {
        return _id ?? string.Empty;
    }

    public override bool Equals(object? obj)
    {
        return obj is EditorInstanceId other && Equals(other);
    }

    public bool Equals(EditorInstanceId other)
    {
        return ToString() == other.ToString();
    }

    public override int GetHashCode()
    {
        return ToString().GetHashCode();
    }

    public static bool operator ==(EditorInstanceId left, EditorInstanceId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EditorInstanceId left, EditorInstanceId right)
    {
        return !left.Equals(right);
    }

    public static implicit operator string(EditorInstanceId id) => id.ToString();
}
