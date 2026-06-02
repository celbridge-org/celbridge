using FileAttributes = System.IO.FileAttributes;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Resolves between resource keys and absolute filesystem paths for a single
/// root, checking that paths stay within the backing folder. Reparse points
/// (symlinks, junctions) are rejected best-effort via File.GetAttributes; the
/// check is not atomic with the following I/O, so it is a containment filter,
/// not a hardened boundary against symlink races.
/// </summary>
[AllowDirectFileSystemAccess]
public class RootPathResolver
{
    private readonly string _rootName;
    private readonly string _backingLocation;
    // Pre-computed once at construction so the GetResourceKey / ValidateAndResolve
    // hot paths don't repeat Path.GetFullPath + TrimEnd on every call.
    private readonly string _normalizedBackingWithSeparator;
    private readonly string _normalizedBackingTrimmed;
    private readonly HashSet<string> _verifiedFolders;

    public RootPathResolver(string rootName, string backingLocation)
    {
        _rootName = rootName;
        _backingLocation = backingLocation;
        _normalizedBackingWithSeparator = NormalizeBackingLocation(backingLocation);
        _normalizedBackingTrimmed = _normalizedBackingWithSeparator
            .TrimEnd(Path.DirectorySeparatorChar);
        _verifiedFolders = new HashSet<string>(GetPathComparer());
    }

    /// <summary>
    /// Validates a resource key and resolves it to an absolute filesystem path under the
    /// resolver's backing location. Returns a failure result if the key fails any validation check.
    /// </summary>
    public Result<string> ValidateAndResolve(ResourceKey resource)
    {
        // Belt-and-suspenders format check. Should never fail since construction already
        // validated, but catches any bypass.
        string resourceKeyString = resource;
        if (!resource.IsEmpty && !ResourceKey.IsValidKey(resourceKeyString))
        {
            return Result<string>.Fail(
                $"Resource key '{resource}' failed format validation.");
        }

        // Resolution operates on the path portion of the key; the root portion has
        // already been used to select the handler and its backing location.
        var pathPortion = resource.Path;

        var combinedPath = Path.Combine(_backingLocation, pathPortion);
        var resolvedPath = Path.GetFullPath(combinedPath);

        var isBackingRoot = resolvedPath.Equals(_normalizedBackingTrimmed, GetPathComparison());

        if (!isBackingRoot && !resolvedPath.StartsWith(_normalizedBackingWithSeparator, GetPathComparison()))
        {
            return Result<string>.Fail(
                $"Resource key '{resource}' resolves to a path outside the '{_rootName}' root.");
        }

        var reparseResult = CheckForReparsePoints(resolvedPath, _normalizedBackingWithSeparator);
        if (reparseResult.IsFailure)
        {
            return Result<string>.Fail(reparseResult.FirstErrorMessage);
        }

        return resolvedPath;
    }

    /// <summary>
    /// Computes the resource key for an absolute filesystem path under this resolver's
    /// backing location. Returns failure when the path is outside the backing location or
    /// the relative segment is not a valid resource key.
    /// </summary>
    public Result<ResourceKey> GetResourceKey(string absolutePath)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(absolutePath);

            // No symlink check here: ValidateAndResolve enforces it at the I/O
            // boundary, and replaying it on every label call would dominate the
            // watcher / enumerate hot path.
            var comparison = GetPathComparison();

            bool isBackingRoot = normalizedPath.Equals(_normalizedBackingTrimmed, comparison);
            bool isUnderBacking = normalizedPath.StartsWith(
                _normalizedBackingWithSeparator, comparison);

            if (!isBackingRoot && !isUnderBacking)
            {
                return Result<ResourceKey>.Fail(
                    $"Path '{absolutePath}' is not under root '{_rootName}' backing location '{_backingLocation}'.");
            }

            var relativePart = isBackingRoot
                ? string.Empty
                : normalizedPath
                    .Substring(_normalizedBackingTrimmed.Length)
                    .Replace('\\', '/')
                    .Trim('/');

            var keyString = string.IsNullOrEmpty(relativePart)
                ? _rootName + ":"
                : _rootName + ":" + relativePart;

            if (!ResourceKey.TryCreate(keyString, out var resourceKey))
            {
                return Result<ResourceKey>.Fail(
                    $"Path '{absolutePath}' produces an invalid resource key: '{keyString}'.");
            }

            return resourceKey;
        }
        catch (Exception ex)
        {
            return Result<ResourceKey>.Fail($"An exception occurred when getting the resource key for '{absolutePath}'.")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Predicate for recursive gateway enumerations. Returns true when the
    /// absolute path is contained within this resolver's backing location and
    /// traverses no reparse point along its segments. Reuses the verified-folder
    /// cache so the hot path costs at most one stat per unseen folder.
    /// </summary>
    public bool IsPathSafe(string absolutePath)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(absolutePath);
            var comparison = GetPathComparison();

            bool isBackingRoot = normalizedPath.Equals(_normalizedBackingTrimmed, comparison);
            bool isUnderBacking = normalizedPath.StartsWith(_normalizedBackingWithSeparator, comparison);

            if (!isBackingRoot
                && !isUnderBacking)
            {
                return false;
            }

            var reparseResult = CheckForReparsePoints(normalizedPath, _normalizedBackingWithSeparator);
            return reparseResult.IsSuccess;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clears the cache of verified directory paths. Call this when the directory
    /// structure may have changed (e.g. after ResourceMonitor triggers a registry sync).
    /// </summary>
    public void InvalidateCache()
    {
        _verifiedFolders.Clear();
    }

    private Result CheckForReparsePoints(string resolvedPath, string normalizedBackingLocation)
    {
        var folderPath = GetFolderPath(resolvedPath);

        if (_verifiedFolders.Contains(folderPath))
        {
            return Result.Ok();
        }

        // When resolvedPath is the backing location itself, there's nothing to check
        var backingTrimmed = normalizedBackingLocation.TrimEnd(Path.DirectorySeparatorChar);
        if (resolvedPath.Equals(backingTrimmed, GetPathComparison()))
        {
            _verifiedFolders.Add(folderPath);
            return Result.Ok();
        }

        var relativePart = resolvedPath.Substring(normalizedBackingLocation.Length);
        var segments = relativePart.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        var currentPath = normalizedBackingLocation.TrimEnd(Path.DirectorySeparatorChar);

        foreach (var segment in segments)
        {
            currentPath = Path.Combine(currentPath, segment);

            if (!File.Exists(currentPath) && !Directory.Exists(currentPath))
            {
                break;
            }

            var attributes = File.GetAttributes(currentPath);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return Result.Fail(
                    $"Resource path contains a symbolic link or junction at '{currentPath}'.");
            }
        }

        _verifiedFolders.Add(folderPath);
        return Result.Ok();
    }

    private static string GetFolderPath(string resolvedPath)
    {
        if (Directory.Exists(resolvedPath))
        {
            return resolvedPath;
        }

        var folder = Path.GetDirectoryName(resolvedPath);
        return folder ?? resolvedPath;
    }

    private static string NormalizeBackingLocation(string backingLocation)
    {
        var normalized = Path.GetFullPath(backingLocation);
        if (!normalized.EndsWith(Path.DirectorySeparatorChar))
        {
            normalized += Path.DirectorySeparatorChar;
        }
        return normalized;
    }

    // StringComparison flavour for string ops; StringComparer flavour for
    // collection keys. Both consult the same Windows / non-Windows selector.
    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }
}
