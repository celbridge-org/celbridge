namespace Celbridge.Documents;

/// <summary>
/// A strongly-typed identifier for a document editor factory.
/// Format: "scope.editor-name" in lowercase kebab-case (e.g., "celbridge.code-editor").
/// </summary>
public readonly struct DocumentEditorId : IEquatable<DocumentEditorId>
{
    private readonly string? _id;

    public DocumentEditorId(string id)
    {
        if (!IsValid(id))
        {
            throw new ArgumentException($"Invalid document editor ID: '{id}'. Expected format: 'scope.editor-name' using lowercase letters, digits, dots, and hyphens.", nameof(id));
        }

        _id = id;
    }

    public static DocumentEditorId Empty => new();

    public bool IsEmpty => string.IsNullOrEmpty(_id);

    /// <summary>
    /// Returns true if the string is a valid document editor ID.
    /// Must contain at least one dot, and use only lowercase letters, digits, dots, and hyphens.
    /// </summary>
    public static bool IsValid(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        bool hasDot = false;
        foreach (var character in id)
        {
            if (character == '.')
            {
                hasDot = true;
            }
            else if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')
            {
                return false;
            }
        }

        return hasDot;
    }

    /// <summary>
    /// Tries to parse a string into a DocumentEditorId without throwing on invalid input.
    /// Returns true on success; false and DocumentEditorId.Empty on failure.
    /// </summary>
    public static bool TryParse(string? id, out DocumentEditorId result)
    {
        if (string.IsNullOrEmpty(id) || !IsValid(id))
        {
            result = Empty;
            return false;
        }

        result = new DocumentEditorId(id);
        return true;
    }

    public override string ToString()
    {
        return _id ?? string.Empty;
    }

    public override bool Equals(object? obj)
    {
        return obj is DocumentEditorId other && Equals(other);
    }

    public bool Equals(DocumentEditorId other)
    {
        return ToString() == other.ToString();
    }

    public override int GetHashCode()
    {
        return ToString().GetHashCode();
    }

    public static bool operator ==(DocumentEditorId left, DocumentEditorId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(DocumentEditorId left, DocumentEditorId right)
    {
        return !left.Equals(right);
    }

    public static implicit operator DocumentEditorId(string id) => new(id);

    public static implicit operator string(DocumentEditorId id) => id.ToString();
}
