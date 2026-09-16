using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// A file to add to a zip archive: the resource to read and the entry name to store it under.
/// </summary>
public record ArchiveSourceFile(ResourceKey Resource, string EntryName);

/// <summary>
/// Static utility methods for creating and extracting zip archives.
/// Used by ArchiveResourceCommand, ExportArchiveCommand and UnarchiveResourceCommand.
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
        IResourceFileSystem resourceFileSystem,
        ResourceKey sourceResource,
        string entryName)
    {
        var openResult = await resourceFileSystem.OpenReadAsync(sourceResource);
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
    /// Lists the files to archive for a file or folder resource. A file is stored under its own name, and a
    /// folder's files under their paths relative to the folder, so the folder's contents sit at the archive
    /// root. The folder walk goes through the gateway, so reserved folders such as .celbridge are never listed.
    /// </summary>
    public static async Task<List<ArchiveSourceFile>> CollectSourceFilesAsync(
        IResourceFileSystem resourceFileSystem,
        ResourceKey sourceResource,
        bool isFolder)
    {
        var sourceFiles = new List<ArchiveSourceFile>();

        if (isFolder)
        {
            await CollectFolderSourceFilesAsync(resourceFileSystem, sourceResource, string.Empty, sourceFiles);
        }
        else
        {
            sourceFiles.Add(new ArchiveSourceFile(sourceResource, sourceResource.ResourceName));
        }

        return sourceFiles;
    }

    /// <summary>
    /// Adds each source file to a zip archive under its entry name, stopping at the first file that
    /// cannot be read.
    /// </summary>
    public static async Task<Result> AddSourceFilesToArchiveAsync(
        ZipArchive zipArchive,
        IResourceFileSystem resourceFileSystem,
        IReadOnlyList<ArchiveSourceFile> sourceFiles)
    {
        foreach (var sourceFile in sourceFiles)
        {
            var addResult = await AddFileToArchiveAsync(zipArchive, resourceFileSystem, sourceFile.Resource, sourceFile.EntryName);
            if (addResult.IsFailure)
            {
                return addResult;
            }
        }

        return Result.Ok();
    }

    // A folder that cannot be listed contributes no files.
    private static async Task CollectFolderSourceFilesAsync(
        IResourceFileSystem resourceFileSystem,
        ResourceKey folder,
        string relativePrefix,
        List<ArchiveSourceFile> sourceFiles)
    {
        var enumerateResult = await resourceFileSystem.EnumerateFolderAsync(folder);
        if (enumerateResult.IsFailure)
        {
            return;
        }

        foreach (var item in enumerateResult.Value)
        {
            var name = item.Resource.ResourceName;
            var childRelative = string.IsNullOrEmpty(relativePrefix)
                ? name
                : $"{relativePrefix}/{name}";

            if (item.IsFolder)
            {
                await CollectFolderSourceFilesAsync(resourceFileSystem, item.Resource, childRelative, sourceFiles);
            }
            else
            {
                sourceFiles.Add(new ArchiveSourceFile(item.Resource, childRelative));
            }
        }
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
            var regexPattern = GlobHelper.GlobToRegex(pattern);
            regexList.Add(new Regex(regexPattern, RegexOptions.IgnoreCase));
        }

        return regexList;
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
