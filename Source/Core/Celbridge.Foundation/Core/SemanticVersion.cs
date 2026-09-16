using System.Globalization;

namespace Celbridge.Core;

/// <summary>
/// A version in the three-part MAJOR.MINOR.PATCH form every Celbridge version uses. The name follows SemVer,
/// but only its three-part core is accepted, with no pre-release or build suffix.
/// </summary>
public readonly struct SemanticVersion : IEquatable<SemanticVersion>, IComparable<SemanticVersion>
{
    /// <summary>
    /// The version an optional version field has when it is missing or empty, which is 1.0.0.
    /// </summary>
    public static SemanticVersion Default { get; } = new SemanticVersion(1, 0, 0);

    /// <summary>
    /// Creates a version from its three parts. Throws ArgumentOutOfRangeException when a part is negative.
    /// </summary>
    public SemanticVersion(int major, int minor, int patch)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);

        Major = major;
        Minor = minor;
        Patch = patch;
    }

    /// <summary>
    /// The first of the three parts.
    /// </summary>
    public int Major { get; }

    /// <summary>
    /// The second of the three parts.
    /// </summary>
    public int Minor { get; }

    /// <summary>
    /// The third of the three parts.
    /// </summary>
    public int Patch { get; }

    /// <summary>
    /// Parses exactly three dot-separated non-negative integers without leading zeros. A v prefix, a
    /// pre-release or build suffix, surrounding whitespace and any other number of parts are rejected.
    /// </summary>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var parts = text.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!TryParsePart(parts[0], out var major) ||
            !TryParsePart(parts[1], out var minor) ||
            !TryParsePart(parts[2], out var patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch);

        return true;
    }

    /// <summary>
    /// Reads the value of an optional version field. A missing or empty value is Default, and a malformed
    /// value fails with a message that names it.
    /// </summary>
    public static Result<SemanticVersion> ParseOptional(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Default;
        }

        if (!TryParse(text, out var version))
        {
            return Result.Fail($"'{text}' is not a three-part version such as {Default}.");
        }

        return version;
    }

    /// <summary>
    /// Formats the version as MAJOR.MINOR.PATCH.
    /// </summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
    }

    public bool Equals(SemanticVersion other)
    {
        return Major == other.Major &&
            Minor == other.Minor &&
            Patch == other.Patch;
    }

    public override bool Equals(object? obj)
    {
        return obj is SemanticVersion other &&
            Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Major, Minor, Patch);
    }

    public int CompareTo(SemanticVersion other)
    {
        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = Minor.CompareTo(other.Minor);
        if (minorComparison != 0)
        {
            return minorComparison;
        }

        return Patch.CompareTo(other.Patch);
    }

    public static bool operator ==(SemanticVersion left, SemanticVersion right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SemanticVersion left, SemanticVersion right)
    {
        return !left.Equals(right);
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator >(SemanticVersion left, SemanticVersion right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator <=(SemanticVersion left, SemanticVersion right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >=(SemanticVersion left, SemanticVersion right)
    {
        return left.CompareTo(right) >= 0;
    }

    // A part is one or more ASCII digits, with no leading zero, that fits in an int.
    private static bool TryParsePart(string part, out int value)
    {
        value = 0;

        if (part.Length == 0)
        {
            return false;
        }

        if (part.Length > 1 &&
            part[0] == '0')
        {
            return false;
        }

        foreach (var character in part)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
