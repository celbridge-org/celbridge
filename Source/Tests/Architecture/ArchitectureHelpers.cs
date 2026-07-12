namespace Celbridge.Tests.Architecture;

/// <summary>
/// Shared helpers for architecture tests that scan the repository's production source files.
/// </summary>
internal static class ArchitectureHelpers
{
    /// <summary>
    /// Locates the repository Source folder by walking up from the test binary to the solution file, or an
    /// empty string if it cannot be found.
    /// </summary>
    public static string FindSourceFolder()
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

    /// <summary>
    /// Enumerates production C# source files under the Source folder, excluding the Tests project and any
    /// generated or build-output files.
    /// </summary>
    public static IEnumerable<string> EnumerateProductionSourceFiles(string sourceFolder)
    {
        // The conventions govern production code. The Tests project legitimately names the guarded concepts.
        var testsFolder = Path.Combine(sourceFolder, "Tests");

        foreach (var filePath in Directory.EnumerateFiles(sourceFolder, "*.cs", SearchOption.AllDirectories))
        {
            if (filePath.StartsWith(testsFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsGeneratedOrBuildOutput(filePath))
            {
                continue;
            }

            yield return filePath;
        }
    }

    private static bool IsGeneratedOrBuildOutput(string filePath)
    {
        var segments = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment is "obj" or "bin");
    }
}
