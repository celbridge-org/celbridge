namespace Celbridge.Tests.Architecture;

/// <summary>
/// Guards the workshop boundary in the tool surface. PackageToolsHandler withholds the workshop_* prefix from
/// package editors, so every tool that reaches the workshop through a workshop API client must be a workshop
/// tool in Celbridge.Tools/Tools/Workshop, where the prefix covers it.
/// </summary>
[TestFixture]
public class WorkshopClientContainmentTests
{
    private static readonly string[] WorkshopClientInterfaces =
    [
        "IPackageApiClient",
        "IPageApiClient",
    ];

    [Test]
    public void WorkshopClients_AreUsedOnlyByWorkshopTools()
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        Directory.Exists(sourceFolder).Should().BeTrue(
            "the repository Source folder must be locatable from the test binary");

        var toolsFiles = ArchitectureHelpers.EnumerateProductionSourceFiles(sourceFolder)
            .Where(filePath => IsInToolsProject(sourceFolder, filePath))
            .ToList();
        toolsFiles.Should().NotBeEmpty("the Celbridge.Tools sources must be locatable under the Source folder");

        var offenders = new List<string>();
        foreach (var filePath in toolsFiles)
        {
            if (IsInWorkshopToolsFolder(sourceFolder, filePath))
            {
                continue;
            }

            var contents = ArchitectureHelpers.ReadSourceFile(filePath);
            if (WorkshopClientInterfaces.Any(clientInterface => contents.Contains(clientInterface)))
            {
                offenders.Add(Path.GetRelativePath(sourceFolder, filePath));
            }
        }

        offenders.Should().BeEmpty(
            "a tool that uses a workshop API client must be a workshop_* tool in Tools/Workshop, so package editors cannot call it");
    }

    private static bool IsInToolsProject(string sourceFolder, string filePath)
    {
        var segments = GetRelativeSegments(sourceFolder, filePath);
        return segments.Length > 2
            && segments[0] == "Core"
            && segments[1] == "Celbridge.Tools";
    }

    private static bool IsInWorkshopToolsFolder(string sourceFolder, string filePath)
    {
        var segments = GetRelativeSegments(sourceFolder, filePath);
        return segments.Length > 4
            && segments[0] == "Core"
            && segments[1] == "Celbridge.Tools"
            && segments[2] == "Tools"
            && segments[3] == "Workshop";
    }

    private static string[] GetRelativeSegments(string sourceFolder, string filePath)
    {
        var relativePath = Path.GetRelativePath(sourceFolder, filePath);
        return relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
