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
        return !(left == right);
    }

    /// <summary>
    // Implicit conversion from string to ResourceKey
    /// </summary>
    public static implicit operator ResourceKey(string key)
    {
        return new ResourceKey(key);
    }

    /// <summary>
    /// Implicit conversion from ResourceKey to string
    /// </summary>
    public static implicit operator string(ResourceKey resourceKey)
    {
        return resourceKey._key;
    }

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
}
