using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Static utility methods for creating and extracting zip archives.
/// Used by ArchiveResourceCommand and UnarchiveResourceCommand.
/// </summary>
public static class ArchiveHelper
{
    /// <summary>
    /// The maximum total uncompressed size allowed when extracting an archive.
    /// </summary>
    public const long MaxExtractedBytes = 256 * 1024 * 1024;

    /// <summary>
    /// Returns true if the zip entry's external attributes indicate a Unix symlink.
    /// The upper 16 bits of ExternalAttributes store the Unix st_mode when the
    /// archive was created on a Unix system. S_IFMT is 0xF000 and S_IFLNK is 0xA000.
    /// </summary>
    public static bool IsUnixSymlink(ZipArchiveEntry entry)
    {
        int unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        return (unixMode & 0xF000) == 0xA000;
    }

    /// <summary>
    /// Adds a project-tree file to a zip archive under the specified entry name,
    /// reading the source through the gateway so containment validation
    /// applies uniformly to archive sources.
    /// </summary>
    public static async Task<Result> AddFileToArchiveAsync(
        ZipArchive zipArchive,
        IFileStorage fileStorage,
        ResourceKey sourceResource,
        string entryName)
    {
        var openResult = await fileStorage.OpenReadAsync(sourceResource);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to read source file for archive: '{sourceResource}'")
                .WithErrors(openResult);
        }

        var entry = zipArchive.CreateEntry(entryName, CompressionLevel.Optimal);

        using var entryStream = entry.Open();
        await using var fileStream = openResult.Value;
        await fileStream.CopyToAsync(entryStream);
        return Result.Ok();
    }

    /// <summary>
    /// Determines whether a file should be included based on include and exclude glob patterns.
    /// Exclude patterns are checked against the file name, each path segment, and the full entry path.
    /// Include patterns are checked against the file name only.
    /// </summary>
    public static bool ShouldIncludeFile(string entryName, List<Regex> includeRegexes, List<Regex> excludeRegexes)
    {
        var fileName = entryName;
        var lastSlashIndex = entryName.LastIndexOf('/');
        if (lastSlashIndex >= 0)
        {
            fileName = entryName.Substring(lastSlashIndex + 1);
        }

        var segments = entryName.Split('/');
        foreach (var excludeRegex in excludeRegexes)
        {
            if (excludeRegex.IsMatch(fileName) || excludeRegex.IsMatch(entryName))
            {
                return false;
            }

            foreach (var segment in segments)
            {
                if (excludeRegex.IsMatch(segment))
                {
                    return false;
                }
            }
        }

        if (includeRegexes.Count == 0)
        {
            return true;
        }

        foreach (var includeRegex in includeRegexes)
        {
            if (includeRegex.IsMatch(fileName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parses a semicolon-separated string of glob patterns into a list of compiled regular expressions.
    /// </summary>
    public static List<Regex> ParseGlobPatterns(string patterns)
    {
        if (string.IsNullOrWhiteSpace(patterns))
        {
            return new List<Regex>();
        }

        var regexList = new List<Regex>();

        var patternParts = patterns.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pattern in patternParts)
        {
            var regexPattern = GlobToRegex(pattern);
            regexList.Add(new Regex(regexPattern, RegexOptions.IgnoreCase));
        }

        return regexList;
    }

    /// <summary>
    /// Converts a simple glob pattern (supporting * and ?) to a regular expression.
    /// </summary>
    public static string GlobToRegex(string glob)
    {
        var regexPattern = Regex.Escape(glob)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return $"^{regexPattern}$";
    }

    /// <summary>
    /// Returns true if the name follows npm package naming conventions: lowercase alphanumeric
    /// and hyphens, 1-214 characters, must start and end with a letter or digit, no consecutive hyphens.
    /// </summary>
    public static bool IsValidPackageName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 214)
        {
            return false;
        }

        return Regex.IsMatch(name, @"^[a-z0-9]([a-z0-9\-]*[a-z0-9])?$") &&
               !name.Contains("--");
    }

    /// <summary>
    /// Returns the path to the local package registry folder in AppData.
    /// </summary>
    public static string GetPackageRegistryPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "Celbridge", "Packages");
    }

    /// <summary>
    /// Returns the full path to a package zip file in the local registry.
    /// </summary>
    public static string GetPackageFilePath(string packageName)
    {
        return Path.Combine(GetPackageRegistryPath(), $"{packageName}.zip");
    }

    /// <summary>
    /// Collects all folders in the hierarchy between a folder path and the destination root,
    /// so they can be created as tracked operations for proper undo support.
    /// </summary>
    public static void CollectFolderHierarchy(
        string folderPath,
        string destinationPath,
        SortedSet<string> foldersToCreate)
    {
        var normalizedDestination = Path.GetFullPath(destinationPath);
        var currentFolder = Path.GetFullPath(folderPath);

        while (!string.IsNullOrEmpty(currentFolder) &&
               currentFolder.Length > normalizedDestination.Length &&
               !string.Equals(currentFolder, normalizedDestination, StringComparison.OrdinalIgnoreCase))
        {
            foldersToCreate.Add(currentFolder);
            currentFolder = Path.GetDirectoryName(currentFolder) ?? string.Empty;
        }
    }
}
