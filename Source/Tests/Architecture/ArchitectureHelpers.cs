using System.Collections.Concurrent;

namespace Celbridge.Tests.Architecture;

/// <summary>
/// Shared helpers for architecture tests that scan the repository's production source files.
/// </summary>
internal static class ArchitectureHelpers
{
    private static readonly Lazy<string> CachedSourceFolder = new(LocateSourceFolder);
    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> CachedFileLists = new();
    private static readonly ConcurrentDictionary<string, string> CachedFileContents = new();

    /// <summary>
    /// Locates the repository Source folder by walking up from the test binary to the solution file, or an
    /// empty string if it cannot be found.
    /// </summary>
    public static string FindSourceFolder()
    {
        return CachedSourceFolder.Value;
    }

    /// <summary>
    /// Enumerates production source files of the given kind under the Source folder, excluding the Tests
    /// project and any generated or build-output files.
    /// </summary>
    public static IEnumerable<string> EnumerateProductionSourceFiles(string sourceFolder, string searchPattern = "*.cs")
    {
        var cacheKey = string.Concat(sourceFolder, "|", searchPattern);

        return CachedFileLists.GetOrAdd(cacheKey, _ => CollectProductionSourceFiles(sourceFolder, searchPattern));
    }

    /// <summary>
    /// Enumerates first-party web source files under the Source folder, excluding build output, third-party
    /// bundles, and web test suites.
    /// </summary>
    public static IEnumerable<string> EnumerateProductionWebFiles(string sourceFolder)
    {
        foreach (var filePath in EnumerateProductionSourceFiles(sourceFolder, "*.js"))
        {
            if (filePath.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var segments = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(segment => segment is "min" or "lib" or "vendor" or "dist" or "tests"))
            {
                continue;
            }

            yield return filePath;
        }
    }

    /// <summary>
    /// Reads a source file located by one of the enumeration helpers, holding the contents so that the
    /// tests which scan the same files do not each read them from disk.
    /// </summary>
    public static string ReadSourceFile(string filePath)
    {
        return CachedFileContents.GetOrAdd(filePath, File.ReadAllText);
    }

    private static string LocateSourceFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null)
        {
            var solutionPath = Path.Combine(folder.FullName, "Celbridge.slnx");
            if (File.Exists(solutionPath))
            {
                return Path.Combine(folder.FullName, "Source");
            }

            folder = folder.Parent;
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> CollectProductionSourceFiles(string sourceFolder, string searchPattern)
    {
        // The conventions govern production code. The Tests project legitimately names the guarded concepts.
        var testsFolder = Path.Combine(sourceFolder, "Tests");

        var filePaths = new List<string>();
        CollectSourceFilesInFolder(sourceFolder, searchPattern, testsFolder, filePaths);

        return filePaths;
    }

    // Walks a folder at a time so build output and cache folders are pruned rather than enumerated and
    // then discarded. A developer machine carries tens of thousands of such files, and walking them
    // dominates the runtime of every test that scans the tree.
    private static void CollectSourceFilesInFolder(
        string folder,
        string searchPattern,
        string testsFolder,
        List<string> filePaths)
    {
        foreach (var filePath in Directory.EnumerateFiles(folder, searchPattern))
        {
            filePaths.Add(filePath);
        }

        foreach (var subFolder in Directory.EnumerateDirectories(folder))
        {
            if (IsExcludedFolder(subFolder, testsFolder))
            {
                continue;
            }

            CollectSourceFilesInFolder(subFolder, searchPattern, testsFolder, filePaths);
        }
    }

    private static bool IsExcludedFolder(string folderPath, string testsFolder)
    {
        if (folderPath.Equals(testsFolder, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var folderName = Path.GetFileName(folderPath);

        // A dot folder holds a dependency cache or per-project runtime data, never checked in source.
        // Some carry a bundled Python or Node install whose own files would otherwise be scanned as
        // if they were Celbridge sources.
        if (folderName.StartsWith('.'))
        {
            return true;
        }

        return folderName is "bin" or "obj" or "node_modules";
    }
}
