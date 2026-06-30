namespace Celbridge.Tests.Architecture;

/// <summary>
/// Guards the cross-platform Platform/ folder convention: native interop declared with DllImport must
/// live only inside a Platform/ folder, so platform code stays discoverable by a single glob. This is
/// the featherweight stand-in for the declined build-time analyzer. The genuine runtime OS-check
/// exceptions (backend tool selection, filesystem case-sensitivity, packaged-WinUI TFM forks) are
/// documented in CLAUDE.md and are deliberately not asserted here.
/// </summary>
[TestFixture]
public class PlatformContainmentTests
{
    [Test]
    public void NativeInterop_LivesOnlyInPlatformFolders()
    {
        var sourceFolder = FindSourceFolder();
        Directory.Exists(sourceFolder).Should().BeTrue(
            "the repository Source folder must be locatable from the test binary");

        var offenders = new List<string>();
        foreach (var filePath in EnumerateProductionSourceFiles(sourceFolder))
        {
            var contents = File.ReadAllText(filePath);
            if (!contents.Contains("DllImport"))
            {
                continue;
            }

            if (!IsInsidePlatformFolder(sourceFolder, filePath))
            {
                offenders.Add(Path.GetRelativePath(sourceFolder, filePath));
            }
        }

        offenders.Should().BeEmpty(
            "native interop must be contained in a Platform/ folder per the cross-platform convention");
    }

    private static string FindSourceFolder()
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

    private static IEnumerable<string> EnumerateProductionSourceFiles(string sourceFolder)
    {
        // The convention governs production code. The Tests project legitimately names platform concepts.
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

    private static bool IsInsidePlatformFolder(string sourceFolder, string filePath)
    {
        var relativePath = Path.GetRelativePath(sourceFolder, filePath);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment == "Platform");
    }
}
