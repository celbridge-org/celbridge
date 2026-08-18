namespace Celbridge.Reports;

/// <summary>
/// The stable identity of a finding kind, carried by every occurrence of it. Host codes read
/// "CEL_&lt;AREA&gt;_&lt;NNN&gt;"; a contribution's code is namespaced by its package as
/// "{package}.{code}", the same dotted form an editor id takes. Opaque to everything that handles
/// one: a reader groups by it and resolves help from it, and nothing takes it apart.
/// </summary>
public readonly struct ReportCode : IEquatable<ReportCode>
{
    private readonly string? _value;

    public ReportCode(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException($"Invalid report code: '{value}'. Expected a non-empty identifier with no whitespace.", nameof(value));
        }

        _value = value;
    }

    /// <summary>
    /// The "no code" value, carried by a fact and by a finding a producer chose not to give a code.
    /// </summary>
    public static ReportCode Empty => new();

    /// <summary>
    /// True when this is the Empty value.
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(_value);

    /// <summary>
    /// Returns true if the string can serve as a code: non-empty, and free of whitespace and control
    /// characters, so it survives a round trip through the report file and a help topic lookup. The
    /// host's own code format is a convention on top of this, held by ReportCodeCoverageTests.
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) ||
                char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Tries to parse a string into a ReportCode without throwing on invalid input. Returns false and
    /// ReportCode.Empty when the string cannot serve as a code.
    /// </summary>
    public static bool TryParse(string? value, out ReportCode result)
    {
        if (!IsValid(value))
        {
            result = Empty;
            return false;
        }

        result = new ReportCode(value!);
        return true;
    }

    public override string ToString()
    {
        return _value ?? string.Empty;
    }

    public override bool Equals(object? obj)
    {
        return obj is ReportCode other && Equals(other);
    }

    public bool Equals(ReportCode other)
    {
        return ToString() == other.ToString();
    }

    public override int GetHashCode()
    {
        return ToString().GetHashCode();
    }

    public static bool operator ==(ReportCode left, ReportCode right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ReportCode left, ReportCode right)
    {
        return !left.Equals(right);
    }

    public static implicit operator string(ReportCode code) => code.ToString();
}
