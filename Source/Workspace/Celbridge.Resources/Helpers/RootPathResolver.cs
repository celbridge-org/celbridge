namespace Celbridge.Resources.Helpers;

/// <summary>
/// Resolves between resource keys and absolute filesystem paths for one root.
/// Refuses a key that resolves outside the backing folder or passes through a
/// link. When the root keeps listings, also refuses a key that differs from the
/// name stored on disk for an existing item. Caches the folders it has checked, with
/// the hashes of their names. A link that replaces a cached folder goes unseen
/// until the folder's removal is recorded. The resolver is therefore a
/// containment filter, not a hardened boundary against symlink races.
/// </summary>
[AllowDirectFileSystemAccess]
public class RootPathResolver
{
    // FileSystemInfo.Attributes reports every flag for an item that does not exist.
    private const System.IO.FileAttributes MissingItemAttributes = (System.IO.FileAttributes)(-1);

    private static readonly char[] PathSeparators =
    {
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar,
    };

    private readonly string _rootName;
    private readonly string _backingLocation;
    // Pre-computed once at construction so the GetResourceKey / ValidateAndResolve
    // hot paths don't repeat Path.GetFullPath + TrimEnd on every call.
    private readonly string _normalizedBackingWithSeparator;
    private readonly string _normalizedBackingTrimmed;
    private readonly FolderCache _folderCache;

    public RootPathResolver(string rootName, string backingLocation, ListingPolicy listingPolicy)
        : this(rootName, backingLocation, listingPolicy, TimeProvider.System)
    {
    }

    internal RootPathResolver(string rootName, string backingLocation, ListingPolicy listingPolicy, TimeProvider timeProvider)
    {
        _rootName = rootName;
        _backingLocation = backingLocation;
        _normalizedBackingWithSeparator = NormalizeBackingLocation(backingLocation);

        // A volume root, like D:\, keeps its separator.
        _normalizedBackingTrimmed = Path.TrimEndingDirectorySeparator(_normalizedBackingWithSeparator);
        _folderCache = new FolderCache(_normalizedBackingTrimmed, listingPolicy, timeProvider);
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

        // Windows drops a trailing dot or space from a name. On every platform, a "." segment names the folder that
        // contains it. Such a key would name an item by a second form of its path.
        var resolvedRelativePath = isBackingRoot
            ? string.Empty
            : resolvedPath.Substring(_normalizedBackingWithSeparator.Length);
        var keyRelativePath = pathPortion.Replace('/', Path.DirectorySeparatorChar);
        if (!string.Equals(resolvedRelativePath, keyRelativePath, StringComparison.Ordinal))
        {
            return Result<string>.Fail(
                $"Resource key '{resource}' resolves to a different path, '{resolvedPath}'.");
        }

        var checkResult = CheckPath(resolvedPath, checkStoredName: true);
        if (checkResult.IsFailure)
        {
            return Result<string>.Fail().WithErrors(checkResult);
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
    /// neither is a link nor passes through one. A listing supplies the path with
    /// the stored names, so the stored name check is skipped. Reuses the folder
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

            var checkResult = CheckPath(normalizedPath, checkStoredName: false);
            return checkResult.IsSuccess;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clears the cache of checked folders and their listings, so those folders are checked and listed
    /// again. Warranted when the watcher has lost events, leaving changes unobserved.
    /// </summary>
    public void InvalidateCache()
    {
        _folderCache.Clear();
    }

    /// <summary>
    /// Records that an item now exists at the absolute path. The item's name joins the listing of the folder
    /// that contains the item. A new folder above the item joins the listing of the folder above the new one.
    /// </summary>
    public void RecordItemCreated(string absolutePath)
    {
        var itemPath = GetFullPathOrNull(absolutePath);
        if (itemPath is not null
            && IsUnderBacking(itemPath))
        {
            _folderCache.RecordItemCreated(itemPath);
        }
    }

    /// <summary>
    /// Records that the item at the absolute path is gone. The item's name leaves the listing of the folder that
    /// contained the item. Every cached folder at or below the path is dropped too. The item may have been a file
    /// or a folder.
    /// </summary>
    public void RecordItemRemoved(string absolutePath)
    {
        var itemPath = GetFullPathOrNull(absolutePath);
        if (itemPath is null)
        {
            // A path that fails to parse could name any cached folder.
            _folderCache.Clear();
            return;
        }

        if (IsUnderBacking(itemPath))
        {
            _folderCache.RecordItemRemoved(itemPath);
        }
    }

    /// <summary>
    /// Records that the name of an item at the absolute path may have changed, without saying how. The listing of
    /// the item's folder is dropped, with every cached folder at or below the path.
    /// </summary>
    public void RecordNameChanged(string absolutePath)
    {
        var itemPath = GetFullPathOrNull(absolutePath);
        if (itemPath is null)
        {
            _folderCache.Clear();
            return;
        }

        if (IsUnderBacking(itemPath))
        {
            _folderCache.RecordNameChanged(itemPath);
        }
    }

    /// <summary>
    /// Returns the number of folders in the cache.
    /// </summary>
    internal int CachedFolderCount => _folderCache.Count;

    /// <summary>
    /// Returns the number of folder listings the resolver has read.
    /// </summary>
    internal int ListingCount => _folderCache.ListingCount;

    // Checks the path below the backing folder. No part may be a link. When stored names are checked, each
    // existing part must be in the listing of the folder above the part. A missing part ends the check, since
    // everything below a missing part is missing too.
    private Result CheckPath(string resolvedPath, bool checkStoredName)
    {
        if (resolvedPath.Equals(_normalizedBackingTrimmed, PathComparison.Comparison))
        {
            return Result.Ok();
        }

        // One attribute read says whether the item exists, whether it is a folder and whether it is a reparse
        // point. The FileInfo keeps the attributes, so the checks below read them again for free.
        var item = new FileInfo(resolvedPath);
        var attributes = item.Attributes;
        bool isFolder = attributes != MissingItemAttributes
            && attributes.HasFlag(System.IO.FileAttributes.Directory);

        // A cached folder was checked when it was cached.
        if (isFolder
            && _folderCache.TryGet(resolvedPath) is not null)
        {
            return Result.Ok();
        }

        // A cached folder says nothing about the item in it, so the item is checked every time.
        var parentPath = Path.GetDirectoryName(resolvedPath) ?? _normalizedBackingTrimmed;
        var parentFolder = _folderCache.TryGet(parentPath);
        if (parentFolder is not null)
        {
            return CheckPart(parentFolder, item, resolvedPath, checkStoredName, out _);
        }

        var relativePart = resolvedPath.Substring(_normalizedBackingWithSeparator.Length);
        var segments = relativePart.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);

        var folder = _folderCache.TryGet(_normalizedBackingTrimmed)
            ?? _folderCache.Add(_normalizedBackingTrimmed, parent: null);
        var currentPath = _normalizedBackingTrimmed;

        for (int index = 0; index < segments.Length; index++)
        {
            currentPath = Path.Combine(currentPath, segments[index]);

            bool isFinalPart = index == segments.Length - 1;
            if (isFinalPart)
            {
                return CheckPart(folder, item, resolvedPath, checkStoredName, out _);
            }

            var cachedFolder = _folderCache.TryGet(currentPath);
            if (cachedFolder is not null)
            {
                folder = cachedFolder;
                continue;
            }

            var partResult = CheckPart(folder, new FileInfo(currentPath), resolvedPath, checkStoredName, out var partFolder);
            if (partResult.IsFailure)
            {
                return partResult;
            }

            if (partFolder is null)
            {
                // A missing part, or a file, has nothing below it.
                return Result.Ok();
            }

            folder = partFolder;
        }

        return Result.Ok();
    }

    // Checks one part of a path in the folder above the part. An existing part must not be a link. When stored
    // names are checked, the part's name must be in the folder's listing. A part that is a folder is cached,
    // and returned.
    private Result CheckPart(
        CachedFolder folder,
        FileInfo part,
        string resolvedPath,
        bool checkStoredName,
        out CachedFolder? partFolder)
    {
        partFolder = null;

        var attributes = part.Attributes;
        if (attributes == MissingItemAttributes)
        {
            // A missing part is not cached, so a link created there later is still caught.
            return Result.Ok();
        }

        if (LinkHelper.IsLink(part))
        {
            return RefuseLink(part.FullName);
        }

        if (checkStoredName
            && _folderCache.HasListings)
        {
            var lookup = _folderCache.FindName(folder, part.Name);
            if (lookup == NameLookup.Unlisted)
            {
                return Result.Fail(
                    $"Resource path '{resolvedPath}' could not be checked, because the folder '{folder.FolderPath}' could not be listed.");
            }

            if (lookup == NameLookup.Missing)
            {
                return RefuseStoredNameMismatch(resolvedPath);
            }
        }

        if (attributes.HasFlag(System.IO.FileAttributes.Directory))
        {
            partFolder = _folderCache.TryGet(part.FullName)
                ?? _folderCache.Add(part.FullName, folder);
        }

        return Result.Ok();
    }

    // Refuses a key that does not match the name stored on disk for an existing item, and gives the stored
    // name. A Windows short name matches no stored name, so the refusal for a short name gives only the key.
    private Result RefuseStoredNameMismatch(string resolvedPath)
    {
        var keyResult = GetResourceKey(resolvedPath);
        if (keyResult.IsFailure)
        {
            return Result.Fail($"Resource path '{resolvedPath}' does not match the names stored on disk.");
        }
        var resource = keyResult.Value;

        ResourceKey? storedResource = null;
        var relativePath = resolvedPath.Substring(_normalizedBackingWithSeparator.Length);
        var storedPath = StoredNameFinder.FindStoredPath(_normalizedBackingTrimmed, relativePath);
        if (storedPath is not null)
        {
            var storedKeyResult = GetResourceKey(storedPath);
            if (storedKeyResult.IsSuccess)
            {
                storedResource = storedKeyResult.Value;
            }
        }

        var error = new StoredNameMismatchError(resource, storedResource);
        return Result.Fail().WithException(error);
    }

    private static Result RefuseLink(string linkPath)
    {
        return Result.Fail(
            $"Resource path contains a symbolic link or junction at '{linkPath}'. Celbridge never follows links.");
    }

    private bool IsUnderBacking(string fullPath)
    {
        return fullPath.Equals(_normalizedBackingTrimmed, PathComparison.Comparison)
            || fullPath.StartsWith(_normalizedBackingWithSeparator, PathComparison.Comparison);
    }

    // Returns the full path with any trailing separator removed, or null for a path that fails to parse.
    private static string? GetFullPathOrNull(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
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
