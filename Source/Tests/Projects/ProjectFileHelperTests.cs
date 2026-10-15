using Celbridge.Projects;
using Celbridge.Tests.FileSystem;
using Celbridge.Utilities;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Tests finding a project file's folder, and finding project files in a folder and on a command line. Also tests
/// finding a second project file beside a project file.
/// </summary>
[TestFixture]
public class ProjectFileHelperTests
{
    private string _folderPath = null!;
    private string _workingFolderPath = null!;

    [SetUp]
    public void Setup()
    {
        _folderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ProjectFileHelperTests),
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_folderPath);

        _workingFolderPath = Path.Combine(_folderPath, "Working");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_folderPath))
        {
            Directory.Delete(_folderPath, recursive: true);
        }
    }

    [Test]
    public async Task FindProjectFiles_FindsProjectFilesInAnyCaseDirectlyInTheFolder()
    {
        var lowerCasePath = WriteFile("acme.celbridge");
        var upperCasePath = WriteFile("Other.CELBRIDGE");
        WriteFile("readme.md");
        WriteFile(Path.Combine("nested", "Nested.celbridge"));

        var findResult = await ProjectFileHelper.FindProjectFilesAsync(_folderPath, TestFileSystem.CreateLocal());

        findResult.IsSuccess.Should().BeTrue(findResult.MessageChain);
        findResult.Value.Should().BeEquivalentTo(lowerCasePath, upperCasePath);
    }

    [Test]
    public async Task FindProjectFiles_MissingFolder_FindsNone()
    {
        var missingFolderPath = Path.Combine(_folderPath, "Missing");

        var findResult = await ProjectFileHelper.FindProjectFilesAsync(missingFolderPath, TestFileSystem.CreateLocal());

        findResult.IsSuccess.Should().BeTrue(findResult.MessageChain);
        findResult.Value.Should().BeEmpty();
    }

    [Test]
    public void GetProjectFolderPath_IsTheFullFolderPathWithNoTrailingSeparator()
    {
        var projectFilePath = Path.Combine(_folderPath, "Acme.celbridge");

        ProjectFileHelper.GetProjectFolderPath(projectFilePath).Should().Be(_folderPath);
    }

    [Test]
    public void GetProjectFolderPath_ProjectAtTheRootOfADrive_IsTheRoot()
    {
        var rootPath = Path.GetPathRoot(_folderPath)!;
        var projectFilePath = Path.Combine(rootPath, "Acme.celbridge");

        ProjectFileHelper.GetProjectFolderPath(projectFilePath).Should().Be(rootPath);
    }

    [Test]
    public void FindProjectFile_OpensTheFirstProjectFileAndIgnoresOtherArguments()
    {
        var firstProjectFile = Path.Combine(Path.GetTempPath(), "First", "First.celbridge");
        var secondProjectFile = Path.Combine(Path.GetTempPath(), "Second", "Second.celbridge");
        var arguments = new List<string>
        {
            "--verbose",
            "notes.txt",
            firstProjectFile,
            secondProjectFile
        };

        var projectFile = ProjectFileHelper.FindProjectFile(arguments, _workingFolderPath);

        projectFile.Should().Be(firstProjectFile);
    }

    [Test]
    public void FindProjectFile_RelativePath_IsTakenRelativeToTheWorkingFolder()
    {
        var relativePath = Path.Combine("Acme", "Acme.celbridge");
        var arguments = new List<string>
        {
            relativePath
        };

        var projectFile = ProjectFileHelper.FindProjectFile(arguments, _workingFolderPath);

        projectFile.Should().Be(Path.Combine(_workingFolderPath, "Acme", "Acme.celbridge"));
    }

    [Test]
    public void FindProjectFile_MatchesTheExtensionInAnyCase()
    {
        var upperCasePath = Path.Combine(_workingFolderPath, "Acme.CELBRIDGE");
        var arguments = new List<string>
        {
            upperCasePath
        };

        var projectFile = ProjectFileHelper.FindProjectFile(arguments, _workingFolderPath);

        projectFile.Should().Be(upperCasePath);
    }

    [Test]
    public void FindProjectFile_NoProjectFile_ReturnsNull()
    {
        var arguments = new List<string>
        {
            "--verbose",
            "notes.txt"
        };

        var projectFile = ProjectFileHelper.FindProjectFile(arguments, _workingFolderPath);

        projectFile.Should().BeNull();
    }

    [Test]
    public void FindOtherProjectFile_OnlyTheProjectFile_ReturnsNull()
    {
        var projectFilePath = Path.Combine(_folderPath, "Acme.celbridge");

        ProjectFileHelper.FindOtherProjectFile(projectFilePath, [projectFilePath]).Should().BeNull();
    }

    [Test]
    public void FindOtherProjectFile_AnotherProjectFile_ReturnsIt()
    {
        var projectFilePath = Path.Combine(_folderPath, "Acme.celbridge");
        var otherProjectFilePath = Path.Combine(_folderPath, "Other.celbridge");

        var otherProjectFile = ProjectFileHelper.FindOtherProjectFile(
            projectFilePath,
            [projectFilePath, otherProjectFilePath]);

        otherProjectFile.Should().Be(otherProjectFilePath);
    }

    [Test]
    public void FindOtherProjectFile_TheSameNameInAnotherCase_FollowsTheFileSystem()
    {
        // A folder listing gives the name as it is on disk. That name can differ in case from the path being opened.
        var projectFilePath = Path.Combine(_folderPath, "acme.celbridge");
        var listedProjectFilePath = Path.Combine(_folderPath, "Acme.celbridge");
        var expected = PathComparison.IsCaseInsensitiveFileSystem ? null : listedProjectFilePath;

        ProjectFileHelper.FindOtherProjectFile(projectFilePath, [listedProjectFilePath]).Should().Be(expected);
    }

    private string WriteFile(string relativePath)
    {
        var filePath = Path.Combine(_folderPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, string.Empty);

        return filePath;
    }
}
