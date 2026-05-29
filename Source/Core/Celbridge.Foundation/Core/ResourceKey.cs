namespace Celbridge.Core;

/// <summary>
/// A unique identifier for project resources.
/// A resource key has the optional URI-style form "root:path"; when no root prefix
/// is supplied, the key resolves under the implicit "project" root.
/// Construction validates the key format; invalid strings throw ArgumentException.
/// Use TryCreate() for non-throwing validation of untrusted input.
/// </summary>
public readonly struct ResourceKey : IEquatable<ResourceKey>, IComparable<ResourceKey>
{
    /// <summary>
    /// The implicit root name used when a resource key has no root prefix.
    /// </summary>
    public const string DefaultRoot = "project";

    // _root is null when this key uses the default "project" root; this lets
    // default(ResourceKey) round-trip with the same semantics as ResourceKey.Empty.
    // _path is null/empty for a root-only key (e.g. "temp:" or the default "").
    private readonly string? _root;
    private readonly string? _path;

    public ResourceKey(string key)
    {
        if (!TryParse(key, out var parsedRoot, out var parsedPath))
        {
            throw new ArgumentException($"Invalid resource key: '{key}'", nameof(key));
        }
        _root = parsedRoot;
        _path = parsedPath;
    }

    private ResourceKey(string? root, string? path)
    {
        _root = root;
        _path = path;
    }

    /// <summary>
    /// Returns an empty resource key.
    /// In some contexts, an empty resource key may refer to the project folder.
    /// </summary>
    public static ResourceKey Empty => new ResourceKey();

    /// <summary>
    /// Creates a new ResourceKey from the specified key string, throwing if the key is invalid.
    /// Equivalent to the constructor; provided for readability at trust boundaries.
    /// </summary>
    public static ResourceKey Create(string key)
    {
        return new ResourceKey(key);
    }

    /// <summary>
    /// Attempts to create a ResourceKey from the specified key string.
    /// Returns true if successful, false if the key is invalid.
    /// </summary>
    public static bool TryCreate(string key, out ResourceKey result)
    {
        if (TryParse(key, out var parsedRoot, out var parsedPath))
        {
            result = new ResourceKey(parsedRoot, parsedPath);
            return true;
        }
        result = Empty;
        return false;
    }

    /// <summary>
    /// The root name for this key (e.g. "project", "temp", "logs"). Always non-empty;
    /// defaults to "project" when the source string had no root prefix.
    /// </summary>
    public string Root => _root ?? DefaultRoot;

    /// <summary>
    /// The path portion only, no root prefix; empty for root-only keys. Use this in any
    /// external context (filesystem paths, URLs, etc.) — the canonical "root:path" form
    /// returned by ToString() is only meaningful to Celbridge's resource APIs.
    /// </summary>
    public string Path => _path ?? string.Empty;

    /// <summary>
    /// The canonical "root:path" form of this key. Always carries the explicit root prefix,
    /// even for the default "project" root. Equivalent to ToString(); retained as an explicit
    /// accessor for sites where intent benefits from being explicit.
    /// </summary>
    public string FullKey => (_root ?? DefaultRoot) + ":" + (_path ?? string.Empty);

    /// <summary>
    /// Canonical serialised form: always "root:path", including the explicit "project:" prefix
    /// for the default root. Matches the literal form the reference scanner detects in file
    /// content, so any resource key surfaced through ToString (tool responses, log messages,
    /// error text, debugger views) can be round-tripped or copy-pasted directly into a quoted
    /// reference without forgetting the prefix.
    ///
    /// This form is only meaningful to Celbridge's resource APIs. For any external context
    /// (filesystem paths, URLs, etc.) use the <see cref="Path"/> accessor instead.
    /// </summary>
    public override string ToString()
    {
        return (_root ?? DefaultRoot) + ":" + (_path ?? string.Empty);
    }

    /// <summary>
    /// Returns true if the resource key's path portion is empty (root-only key).
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(_path);

    public override bool Equals(object? obj)
    {
        return obj is not null &&
            obj is ResourceKey other &&
            Equals(other);
    }

    public bool Equals(ResourceKey other)
    {
        return Root == other.Root &&
            Path == other.Path;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Root, Path);
    }

    public int CompareTo(ResourceKey other)
    {
        return string.Compare(FullKey, other.FullKey, StringComparison.Ordinal);
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
    /// Implicit conversion from string to ResourceKey.
    /// Throws ArgumentException if the string is not a valid resource key.
    /// </summary>
    public static implicit operator ResourceKey(string key) => new ResourceKey(key);

    /// <summary>
    /// Implicit conversion from ResourceKey to string
    /// </summary>
    public static implicit operator string(ResourceKey resource) => resource.ToString();

    /// <summary>
    /// Returns the resource name. This is the last segment of the resource key's path.
    /// </summary>
    public string ResourceName
    {
        get
        {
            var path = _path;
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            int lastIndex = path.LastIndexOf('/');
            if (lastIndex == -1)
            {
                return path;
            }

            return path.Substring(lastIndex + 1);
        }
    }

    /// <summary>
    /// Returns the resource name without the file extension.
    /// </summary>
    public string ResourceNameNoExtension
    {
        get
        {
            var resourceName = ResourceName;

            int lastIndex = resourceName.LastIndexOf('.');
            if (lastIndex == -1)
            {
                return resourceName;
            }

            return resourceName.Substring(0, lastIndex);
        }
    }

    /// <summary>
    /// Returns the parent resource key for this key. The root is preserved; the path
    /// loses its last segment. The parent of a root-only key is the same root-only key.
    /// </summary>
    public ResourceKey GetParent()
    {
        var path = _path;
        if (string.IsNullOrEmpty(path))
        {
            return new ResourceKey(_root, null);
        }

        int lastSlashIndex = path.LastIndexOf('/');
        if (lastSlashIndex == -1)
        {
            return new ResourceKey(_root, null);
        }

        var parentPath = path.Substring(0, lastSlashIndex);
        return new ResourceKey(_root, parentPath);
    }

    /// <summary>
    /// Returns true if this resource is a descendant of the specified folder.
    /// A resource is a descendant if it shares the same root and its path starts with
    /// the folder path followed by "/". The root-only key (empty path) is the ancestor
    /// of every non-empty key under the same root.
    /// </summary>
    public bool IsDescendantOf(ResourceKey folderKey)
    {
        if (Root != folderKey.Root)
        {
            return false;
        }

        var folderPath = (folderKey._path ?? string.Empty).TrimEnd('/');

        if (string.IsNullOrEmpty(folderPath))
        {
            // Everything under the same root is a descendant of the root-only key
            // (except a root-only key itself, which has no path).
            return !string.IsNullOrEmpty(_path);
        }

        return _path?.StartsWith(folderPath + "/", StringComparison.Ordinal) ?? false;
    }

    /// <summary>
    /// Returns a new ResourceKey that is the combination of the current key and the specified segment.
    /// The root is preserved; the segment is appended to the path.
    /// </summary>
    public ResourceKey Combine(string segment)
    {
        ArgumentException.ThrowIfNullOrEmpty(segment);

        if (!IsValidSegment(segment))
        {
            throw new ArgumentException($"Invalid resource key segment: '{segment}'", nameof(segment));
        }

        if (segment.Contains('/'))
        {
            throw new ArgumentException($"Segment must not contain path separators: '{segment}'", nameof(segment));
        }

        var combinedPath = string.IsNullOrEmpty(_path) ? segment : _path + "/" + segment;
        return new ResourceKey(_root, combinedPath);
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
        var invalidChars = System.IO.Path.GetInvalidFileNameChars();

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
    /// Resource keys have the optional form "root:path" with the following constraints:
    /// - The optional root prefix matches "[a-z][a-z0-9_]+:" (at least two characters before the colon).
    /// - The path is relative to the root's backing folder.
    /// - Absolute paths, parent and same directory references are not supported.
    /// - '/' is used as the path separator on all platforms; backslashes are not allowed.
    /// </summary>
    public static bool IsValidKey(string key)
    {
        return TryParse(key, out _, out _);
    }

    private static bool TryParse(string key, out string? root, out string? path)
    {
        root = null;
        path = null;

        if (key.Length == 0)
        {
            // An empty resource key is valid (default "project" root, empty path).
            return true;
        }

        // Strip an optional root prefix of the form "[a-z][a-z0-9_]+:".
        // The shortest legal root is two characters (e.g. "ab:"). Empty roots,
        // single-character roots, and uppercase roots are rejected.
        var pathPortion = key;
        var colonIndex = key.IndexOf(':');
        if (colonIndex != -1)
        {
            var rootCandidate = key.Substring(0, colonIndex);
            if (!IsValidRoot(rootCandidate))
            {
                return false;
            }

            if (rootCandidate != DefaultRoot)
            {
                root = rootCandidate;
            }

            pathPortion = key.Substring(colonIndex + 1);
        }

        if (pathPortion.Length == 0)
        {
            // Root-only form (e.g. "project:", "temp:") is valid; path is empty.
            path = null;
            return true;
        }

        if (!IsValidPath(pathPortion))
        {
            return false;
        }

        path = pathPortion;
        return true;
    }

    private static bool IsValidRoot(string rootCandidate)
    {
        // [a-z][a-z0-9_]+ — first char must be a lowercase letter, total length at least 2.
        if (rootCandidate.Length < 2)
        {
            return false;
        }

        var first = rootCandidate[0];
        if (first < 'a' || first > 'z')
        {
            return false;
        }

        for (int i = 1; i < rootCandidate.Length; i++)
        {
            var c = rootCandidate[i];
            bool isLower = c >= 'a' && c <= 'z';
            bool isDigit = c >= '0' && c <= '9';
            bool isUnderscore = c == '_';
            if (!isLower && !isDigit && !isUnderscore)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidPath(string path)
    {
        // Backslashes are not permitted
        if (path.Contains('\\'))
        {
            return false;
        }

        // Empty segments are not permitted
        if (path.Contains("//"))
        {
            return false;
        }

        // Resource keys must represent a relative path
        if (System.IO.Path.IsPathRooted(path))
        {
            return false;
        }

        // Resource keys may not contain parent or same directory references
        if (path.Contains("..") ||
            path.Contains("./"))
        {
            return false;
        }

        // Resource keys may not start or end with a separator character
        if (path[0] == '/' || path[^1] == '/')
        {
            return false;
        }

        // Each segment in the resource key path must be a valid filename.
        // Note: This constraint may prove to be too restrictive for cross-platform projects which
        // work with exotic file names. If this proves to be a problem we could relax this constraint in the future.
        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            if (!IsValidSegment(segment))
            {
                return false;
            }
        }

        return true;
    }
}
