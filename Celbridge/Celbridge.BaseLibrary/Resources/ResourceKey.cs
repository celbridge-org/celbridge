namespace Celbridge.BaseLibrary.Resources;

/// <summary>
/// A unique identifier for project resources.
/// This key is based on the relative path of the resource in the project folder.
/// </summary>
public readonly struct ResourceKey : IEquatable<ResourceKey>, IComparable<ResourceKey>
{
    private readonly string _key = string.Empty;

    public ResourceKey(string key)
    {
        _key = key;
    }

    public override string ToString()
    {
        return _key;
    }

    /// <summary>
    /// Returns true if the resource key is empty.
    /// </summary>
    public bool IsEmpty => _key.Length == 0;

    public override bool Equals(object? obj)
    {
        return obj is not null && 
            obj is ResourceKey other && 
            Equals(other);
    }

    public bool Equals(ResourceKey other)
    {
        return _key == other._key;
    }

    public override int GetHashCode()
    {
        return _key.GetHashCode();
    }

    public int CompareTo(ResourceKey other)
    {
        return string.Compare(_key, other._key, StringComparison.Ordinal);
    }

    public static bool operator ==(ResourceKey left, ResourceKey right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ResourceKey left, ResourceKey right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    // Implicit conversion from string to ResourceKey
    /// </summary>
    public static implicit operator ResourceKey(string key) => new ResourceKey(key);

    /// <summary>
    /// Implicit conversion from ResourceKey to string
    /// </summary>
    public static implicit operator string(ResourceKey resource) => resource._key;

    /// <summary>
    /// Returns the resource name. This is the last segment of the resource key.
    /// </summary>
    public string ResourceName
    {
        get
        {
            if (string.IsNullOrEmpty(_key))
            {
                return string.Empty;
            }

            int lastIndex = _key.LastIndexOf('/');
            if (lastIndex == -1)
            {
                return _key;
            }

            return _key.Substring(lastIndex + 1);
        }
    }

    /// <summary>
    /// Returns the parent resource key for the specified resource key.
    /// </summary>
    public ResourceKey GetParent()
    {
        if (string.IsNullOrEmpty(_key))
        {
            return new ResourceKey(string.Empty);
        }

        int lastSlashIndex = _key.LastIndexOf('/');
        if (lastSlashIndex == -1)
        {
            return new ResourceKey(string.Empty);
        }

        var parentKey = _key.Substring(0, lastSlashIndex);
        return new ResourceKey(parentKey);
    }

    /// <summary>
    /// Returns a new ResourceKey that is the combination of the current key and the specified segment.
    /// </summary>
    public ResourceKey Combine(string segment)
    {
        // Todo: Validate segment properly
        if (string.IsNullOrEmpty(segment))
        {
            throw new ArgumentException();
        }

        if (string.IsNullOrEmpty(_key))
        {
            return new ResourceKey(segment);
        }

        return new ResourceKey(_key + "/" + segment);
    }

    /// <summary>
    /// Returns true if the string represents a valid resource key segment.
    /// </summary>
    public static bool IsValidSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        // The GetInvalidFileNameChars() method returns an array of characters that are not allowed in file names.
        // Unfortunately, this array is different on different platforms. For example, on Windows, ':' is not allowed.
        // On Linux, ':' is a valid character in a file name. This could cause problems for some cross-platform projects.
        var invalidChars = Path.GetInvalidFileNameChars();

        foreach (var c in segment)
        {
            if (invalidChars.Contains(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns true if the string represents a valid resource key.
    /// Resource keys look similar to regular file paths but with additional constraints:
    /// - Specified relative to the project folder. 
    /// - Absolute paths, parent and same directory references are not supported.
    /// - '/' is used as the path separator on all platforms, backslashes are not allowed.
    public static bool IsValidKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            // An empty resource key is valid, and refers to the project folder.
            return true;
        }

        // Backslashes are not permitted
        if (key.Contains("\\"))
        {
            return false;
        }

        // Empty segments are not permitted
        if (key.Contains("//"))
        {
            return false;
        }

        // Resource keys must represent a relative path
        if (Path.IsPathRooted(key))
        {
            return false;
        }

        // Resource keys may not contain parent or same directory references
        if (key.Contains("..") ||
            key.Contains("./") ||
            key.Contains(".\\"))
        {
            return false;
        }

        // Resource keys may not start with a separator character
        if (key[0] == '/')
        {
            return false;
        }

        // Each segment in the resource key must be a valid filename
        // Note: This constraint may prove to be too restrictive for cross-platform projects which
        // work with exotic file names. If this proves to be a problem we could relax this constraint in the future.
        var resourceKeySegments = key.Split('/');
        foreach (var segment in resourceKeySegments)
        {
            if (!IsValidSegment(segment))
            {
                return false;
            }
        }

        return true;
    }
}
