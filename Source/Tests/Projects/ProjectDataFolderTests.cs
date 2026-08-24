using Celbridge.Projects;
using Celbridge.Projects.Services;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Unit tests for the project data folder: the validation that keeps a user-supplied name inside the
/// reserved .celbridge/ folder, and the path every consumer of that folder composes on top of.
/// </summary>
[TestFixture]
public class ProjectDataFolderTests
{
    private static string ProjectFolderPath =>
        OperatingSystem.IsWindows() ? @"C:\Projects\Acme" : "/projects/acme";

    [TestCase("variant-a")]
    [TestCase("designer")]
    [TestCase("data 2")]
    public void IsValidFolderName_SingleSegment_IsAccepted(string folderName)
    {
        ProjectDataFolder.IsValidFolderName(folderName).Should().BeTrue();
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(".")]
    [TestCase("..")]
    [TestCase("../escape")]
    [TestCase("nested/folder")]
    [TestCase("nested\folder")]
    public void IsValidFolderName_AnythingThatIsNotASingleSegment_IsRejected(string folderName)
    {
        ProjectDataFolder.IsValidFolderName(folderName).Should().BeFalse();
    }

    [Test]
    public void IsValidFolderName_AbsolutePath_IsRejected()
    {
        // The name builds paths under a folder the resource layer reserves, so a value that resolves
        // anywhere else on disk has to be refused rather than trimmed to its last segment.
        ProjectDataFolder.IsValidFolderName(ProjectFolderPath).Should().BeFalse();
    }

    [Test]
    public void ResolvePath_NoFolderNamed_IsTheCelbridgeFolderItself()
    {
        var dataFolderPath = ProjectDataFolder.ResolvePath(ProjectFolderPath, string.Empty);

        dataFolderPath.Should().Be(Path.Combine(ProjectFolderPath, ProjectConstants.CelbridgeFolder));
    }

    [Test]
    public void ResolvePath_FolderNamed_SitsUnderTheCelbridgeFolder()
    {
        var dataFolderPath = ProjectDataFolder.ResolvePath(ProjectFolderPath, "variant-a");

        dataFolderPath.Should().Be(
            Path.Combine(ProjectFolderPath, ProjectConstants.CelbridgeFolder, "variant-a"));
    }

    [Test]
    public void ResolvePath_UnusableFolderName_FallsBackToTheCelbridgeFolder()
    {
        var dataFolderPath = ProjectDataFolder.ResolvePath(ProjectFolderPath, "../escape");

        dataFolderPath.Should().Be(Path.Combine(ProjectFolderPath, ProjectConstants.CelbridgeFolder));
    }

    [Test]
    public void ProjectDataFolderPath_FollowsTheConfig()
    {
        // Everything under the data folder (temp/, logs/, utils/, trash/, python/, settings/) is composed
        // from this one path, so the project carrying it is what makes two configurations separate.
        var withoutSetting = CreateProject(string.Empty);
        withoutSetting.ProjectDataFolderPath.Should().Be(
            Path.Combine(ProjectFolderPath, ProjectConstants.CelbridgeFolder));

        var withSetting = CreateProject("variant-a");
        withSetting.ProjectDataFolderPath.Should().Be(
            Path.Combine(ProjectFolderPath, ProjectConstants.CelbridgeFolder, "variant-a"));
    }

    private static Project CreateProject(string dataFolder)
    {
        var config = new ProjectConfig
        {
            Celbridge = new CelbridgeSection
            {
                DataFolder = dataFolder
            }
        };

        return new Project(
            Path.Combine(ProjectFolderPath, "Acme.celbridge"),
            "Acme",
            ProjectFolderPath,
            config,
            MigrationResult.Success(),
            ConfigIsHealthy: true,
            ConfigLoadFailure: null);
    }
}
