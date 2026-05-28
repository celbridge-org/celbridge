using FileAttributes = System.IO.FileAttributes;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Validates that resolved resource paths stay within the backing folder of a single
/// root and do not traverse through symlinks, junctions, or other reparse points.
/// Maintains a cache of verified directory paths to avoid repeated filesystem stat
/// calls. One instance serves exactly one root; its owning root handler constructs
/// it with that root's name and backing location.
/// </summary>
public class PathValidator
{
    private readonly string _rootName;
    private readonly string _backingLocation;
    private readonly StringComparer _pathComparer;
    private readonly HashSet<string> _verifiedFolders;

    public PathValidator(string rootName, string backingLocation)
    {
        _rootName = rootName;
        _backingLocation = backingLocation;
        _pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        _verifiedFolders = new HashSet<string>(_pathComparer);
    }

    /// <summary>
    /// Validates a resource key and resolves it to an absolute filesystem path under the
    /// validator's backing location. Returns a failure result if the key fails any validation check.
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

        var normalizedBackingLocation = NormalizeBackingLocation(_backingLocation);

        var isBackingRoot = resolvedPath.Equals(
            normalizedBackingLocation.TrimEnd(Path.DirectorySeparatorChar),
            GetPathComparison());

        if (!isBackingRoot && !resolvedPath.StartsWith(normalizedBackingLocation, GetPathComparison()))
        {
            return Result<string>.Fail(
                $"Resource key '{resource}' resolves to a path outside the '{_rootName}' root.");
        }

        var reparseResult = CheckForReparsePoints(resolvedPath, normalizedBackingLocation);
        if (reparseResult.IsFailure)
        {
            return Result<string>.Fail(reparseResult.FirstErrorMessage);
        }

        return resolvedPath;
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

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}
