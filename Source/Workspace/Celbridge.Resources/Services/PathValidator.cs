using FileAttributes = System.IO.FileAttributes;

namespace Celbridge.Resources.Services;

/// <summary>
/// Validates that resolved resource paths stay within the project folder and do not traverse
/// through symlinks, junctions, or other reparse points. Maintains a cache of verified
/// directory paths to avoid repeated filesystem stat calls.
/// </summary>
public class PathValidator
{
    private readonly HashSet<string> _verifiedFolders;

    public PathValidator()
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        _verifiedFolders = new HashSet<string>(comparer);
    }

    /// <summary>
    /// Validates a resource key and resolves it to an absolute filesystem path within the
    /// project folder. Returns a failure result if the key fails any validation check.
    /// </summary>
    public Result<string> ValidateAndResolve(string projectFolderPath, ResourceKey resource)
    {
        // Belt-and-suspenders format check. Should never fail since construction already
        // validated, but catches any bypass.
        string resourceKeyString = resource;
        if (!resource.IsEmpty && !ResourceKey.IsValidKey(resourceKeyString))
        {
            return Result<string>.Fail(
                $"Resource key '{resource}' failed format validation.");
        }

        var combinedPath = Path.Combine(projectFolderPath, resourceKeyString);
        var resolvedPath = Path.GetFullPath(combinedPath);

        var normalizedProjectFolder = NormalizeProjectFolder(projectFolderPath);

        var isProjectRoot = resolvedPath.Equals(
            normalizedProjectFolder.TrimEnd(Path.DirectorySeparatorChar),
            GetPathComparison());

        if (!isProjectRoot && !resolvedPath.StartsWith(normalizedProjectFolder, GetPathComparison()))
        {
            return Result<string>.Fail(
                $"Resource key '{resource}' resolves to a path outside the project folder.");
        }

        var reparseResult = CheckForReparsePoints(resolvedPath, normalizedProjectFolder);
        if (reparseResult.IsFailure)
        {
            return Result<string>.Fail(reparseResult.FirstErrorMessage);
        }

        return Result<string>.Ok(resolvedPath);
    }

    /// <summary>
    /// Clears the cache of verified directory paths. Call this when the directory structure
    /// may have changed (e.g. after ResourceMonitor triggers a registry sync).
    /// </summary>
    public void InvalidateCache()
    {
        _verifiedFolders.Clear();
    }

    private Result CheckForReparsePoints(string resolvedPath, string normalizedProjectFolder)
    {
        var folderPath = GetFolderPath(resolvedPath);

        if (_verifiedFolders.Contains(folderPath))
        {
            return Result.Ok();
        }

        // When resolvedPath is the project folder itself, there's nothing to check
        var projectFolderTrimmed = normalizedProjectFolder.TrimEnd(Path.DirectorySeparatorChar);
        if (resolvedPath.Equals(projectFolderTrimmed, GetPathComparison()))
        {
            _verifiedFolders.Add(folderPath);
            return Result.Ok();
        }

        var relativePart = resolvedPath.Substring(normalizedProjectFolder.Length);
        var segments = relativePart.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        var currentPath = normalizedProjectFolder.TrimEnd(Path.DirectorySeparatorChar);

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

    private static string NormalizeProjectFolder(string projectFolderPath)
    {
        var normalized = Path.GetFullPath(projectFolderPath);
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
