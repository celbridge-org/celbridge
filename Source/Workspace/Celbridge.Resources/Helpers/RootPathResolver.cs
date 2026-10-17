using System.Collections.Concurrent;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Resolves between resource keys and absolute filesystem paths for a single
/// root, checking that paths stay within the backing folder. Containment is
/// re-checked on every call. A path that is a link or passes through one is
/// refused. Each folder on the path is checked once for the life of the
/// resolver, and the item itself on every call. The resolver misses a link
/// that replaces a folder after the folder has been verified. It is a
/// containment filter, not a hardened boundary against symlink races.
/// </summary>
[AllowDirectFileSystemAccess]
public class RootPathResolver
{
    // Safety valve for a session that touches an unbounded number of folders. An entry costs one
    // verification to rebuild, so clearing wholesale is enough.
    private const int MaxVerifiedFolders = 100_000;

    private readonly string _rootName;
    private readonly string _backingLocation;
    // Pre-computed once at construction so the GetResourceKey / ValidateAndResolve
    // hot paths don't repeat Path.GetFullPath + TrimEnd on every call.
    private readonly string _normalizedBackingWithSeparator;
    private readonly string _normalizedBackingTrimmed;
    // Resolution runs on the scanner's parallel workers as well as the tree walk. The count is tracked
    // separately because ConcurrentDictionary.Count locks every bucket.
    private readonly ConcurrentDictionary<string, byte> _verifiedFolders;
    private int _verifiedFolderCount;

    public RootPathResolver(string rootName, string backingLocation)
    {
        _rootName = rootName;
        _backingLocation = backingLocation;
        _normalizedBackingWithSeparator = NormalizeBackingLocation(backingLocation);
        _normalizedBackingTrimmed = _normalizedBackingWithSeparator
            .TrimEnd(Path.DirectorySeparatorChar);
        _verifiedFolders = new ConcurrentDictionary<string, byte>(PathComparison.Comparer);
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

        // Resolution operates on the path portion of the key. The root portion has
        // already been used to select the handler and its backing location.
        var pathPortion = resource.Path;

        var combinedPath = Path.Combine(_backingLocation, pathPortion);
        var resolvedPath = Path.GetFullPath(combinedPath);

        var isBackingRoot = resolvedPath.Equals(_normalizedBackingTrimmed, PathComparison.Comparison);

        if (!isBackingRoot && !resolvedPath.StartsWith(_normalizedBackingWithSeparator, PathComparison.Comparison))
        {
            return Result<string>.Fail(
                $"Resource key '{resource}' resolves to a path outside the '{_rootName}' root.");
        }

        var linkResult = CheckForLinks(resolvedPath, _normalizedBackingWithSeparator);
        if (linkResult.IsFailure)
        {
            return Result<string>.Fail(linkResult.FirstErrorMessage);
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
            var comparison = PathComparison.Comparison;

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
    /// neither is a link nor passes through one. Reuses the verified-folder
    /// cache so the hot path costs at most one stat per unseen folder, plus one
    /// for the item itself.
    /// </summary>
    public bool IsPathSafe(string absolutePath)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(absolutePath);
            var comparison = PathComparison.Comparison;

            bool isBackingRoot = normalizedPath.Equals(_normalizedBackingTrimmed, comparison);
            bool isUnderBacking = normalizedPath.StartsWith(_normalizedBackingWithSeparator, comparison);

            if (!isBackingRoot
                && !isUnderBacking)
            {
                return false;
            }

            var linkResult = CheckForLinks(normalizedPath, _normalizedBackingWithSeparator);
            return linkResult.IsSuccess;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clears the cache of verified directory paths so those folders are checked for links
    /// again. Warranted when the watcher has lost events, leaving changes to the folder structure
    /// unobserved.
    /// </summary>
    public void InvalidateCache()
    {
        _verifiedFolders.Clear();
        Interlocked.Exchange(ref _verifiedFolderCount, 0);
    }

    private Result CheckForLinks(string resolvedPath, string normalizedBackingLocation)
    {
        var folderPath = GetFolderPath(resolvedPath);

        if (_verifiedFolders.ContainsKey(folderPath))
        {
            // A verified folder says nothing about the file in it, so the file is checked every time.
            bool isFolder = resolvedPath.Equals(folderPath, PathComparison.Comparison);
            if (!isFolder
                && LinkHelper.IsLink(resolvedPath))
            {
                return RefuseLink(resolvedPath);
            }

            return Result.Ok();
        }

        // When resolvedPath is the backing location itself, there's nothing to check
        var backingTrimmed = normalizedBackingLocation.TrimEnd(Path.DirectorySeparatorChar);
        if (resolvedPath.Equals(backingTrimmed, PathComparison.Comparison))
        {
            AddVerifiedFolder(folderPath);
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

            if (LinkHelper.IsLink(new FileInfo(currentPath)))
            {
                return RefuseLink(currentPath);
            }
        }

        AddVerifiedFolder(folderPath);
        return Result.Ok();
    }

    private static Result RefuseLink(string linkPath)
    {
        return Result.Fail(
            $"Resource path contains a symbolic link or junction at '{linkPath}'. Celbridge never follows links.");
    }

    // The count drifts when threads verify the same folder at once; the valve then trips early, which
    // costs one re-verification per folder.
    private void AddVerifiedFolder(string folderPath)
    {
        _verifiedFolders[folderPath] = 0;

        if (Interlocked.Increment(ref _verifiedFolderCount) >= MaxVerifiedFolders)
        {
            InvalidateCache();
        }
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
}
