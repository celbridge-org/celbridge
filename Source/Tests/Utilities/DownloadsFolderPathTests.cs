using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Resources.Services.Roots;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Utilities;

/// <summary>
/// A project names the folder its downloads are saved to by a path from the project root. These tests pin
/// which paths name a folder at all, and that a folder Celbridge reserves is not one. A named folder is used
/// whether or not the project has made the folder yet. A path the project cannot use sends downloads to the
/// default folder, and the download goes ahead. A path that does not match the folder's name on disk is refused.
/// </summary>
[TestFixture]
public class DownloadsFolderPathTests
{
    private IResourceRegistry _registry = null!;
    private ILocalFileSystem _fileSystem = null!;
    private string _projectFolderPath = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(Path.GetTempPath(), $"DownloadsFolderPathTests_{Guid.NewGuid():N}");
        _fileSystem = TestFileSystem.CreateLocal();
        Directory.CreateDirectory(_projectFolderPath);

        // The project root's real resolver, so a key that does not match a name on disk is refused as in the app.
        var rootHandlers = new RootHandlerRegistry();
        rootHandlers.RegisterRootHandler(new ProjectRootHandler(_projectFolderPath));

        _registry = Substitute.For<IResourceRegistry>();
        _registry.ResolveResourcePath(Arg.Any<ResourceKey>(), Arg.Any<bool>())
            .Returns(callInfo => rootHandlers.ResolveResourcePath(callInfo.Arg<ResourceKey>()));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            Directory.Delete(_projectFolderPath, recursive: true);
        }
    }

    [TestCase("downloads", "downloads")]
    [TestCase("assets/downloads", "assets/downloads")]
    [TestCase(" downloads ", "downloads")]
    [TestCase("/assets/downloads/", "assets/downloads")]
    [TestCase("project:assets/downloads", "assets/downloads")]
    public void AFolderPath_NamesTheFolder(string path, string expectedPath)
    {
        var isParsed = DownloadsFolderPath.TryParse(path, out var folder);

        isParsed.Should().BeTrue();
        folder.Should().Be(new ResourceKey(expectedPath));
    }

    [TestCase("", Description = "nothing")]
    [TestCase("/", Description = "the project root")]
    [TestCase(".", Description = "the project root, written as a dot")]
    [TestCase("../outside", Description = "a path that leaves the project")]
    [TestCase("assets//downloads", Description = "an empty segment")]
    [TestCase("assets\\downloads", Description = "a backslash")]
    [TestCase("temp:downloads", Description = "a folder under another root")]
    public void AnythingElse_NamesNoFolder(string path)
    {
        var isParsed = DownloadsFolderPath.TryParse(path, out _);

        isParsed.Should().BeFalse();
        DownloadsFolderPath.IsReserved(path).Should().BeFalse();
    }

    [TestCase(".git")]
    [TestCase(".git/downloads")]
    [TestCase("assets/.git")]
    [TestCase("vendor/.git/hooks")]
    public void AFolderCelbridgeReserves_NamesNoFolder(string path)
    {
        // Nothing can be saved there, so a downloads folder there would refuse every download.
        var isParsed = DownloadsFolderPath.TryParse(path, out _);

        isParsed.Should().BeFalse();
        DownloadsFolderPath.IsReserved(path).Should().BeTrue();
    }

    [TestCase(".github")]
    [TestCase("git")]
    [TestCase("assets/.gitkeep")]
    public void ANameThatOnlyResemblesAReservedOne_NamesTheFolder(string path)
    {
        var isParsed = DownloadsFolderPath.TryParse(path, out var folder);

        isParsed.Should().BeTrue();
        folder.Should().Be(new ResourceKey(path));
        DownloadsFolderPath.IsReserved(path).Should().BeFalse();
    }

    // The resource policy is what refuses a write, so the folder check has to agree with it about every
    // path, or the settings would accept a folder that every download then fails in.
    [TestCase(".git", true)]
    [TestCase("assets/.git", true)]
    [TestCase("assets/.celbridge", false)]
    [TestCase(".github", false)]
    [TestCase("assets/incoming", false)]
    public void TheReservedCheck_AgreesWithTheResourcePolicy(string path, bool isReserved)
    {
        var project = Substitute.For<IProject>();
        project.Config.Returns(new ProjectConfig());
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);
        var policy = new ResourcePolicy(projectService);

        var writeResult = policy.Evaluate(new ResourceKey($"{path}/payload.bin"), ResourceAction.Write);

        writeResult.IsFailure.Should().Be(isReserved);
        DownloadsFolderPath.IsReserved(path).Should().Be(isReserved);
    }

    [Test]
    public void AFolderTheProjectHas_IsWhereDownloadsGo()
    {
        Directory.CreateDirectory(ResolvePathOnDisk("assets/incoming"));

        var folderResult = DownloadsFolderPath.Resolve(_registry, _fileSystem, "assets/incoming");

        folderResult.IsSuccess.Should().BeTrue();
        folderResult.Value.Should().Be(new ResourceKey("assets/incoming"));
    }

    [Test]
    public void AFolderTheProjectHasNotMade_IsWhereDownloadsGo()
    {
        var folderResult = DownloadsFolderPath.Resolve(_registry, _fileSystem, "assets/incoming");

        folderResult.IsSuccess.Should().BeTrue();
        folderResult.Value.Should().Be(new ResourceKey("assets/incoming"));
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = "On a case-sensitive file system, Incoming and incoming are different folders.")]
    public void AFolderNamedInAnotherCase_IsRefusedWithItsStoredName()
    {
        Directory.CreateDirectory(ResolvePathOnDisk("Incoming"));

        var folderResult = DownloadsFolderPath.Resolve(_registry, _fileSystem, "incoming");

        folderResult.IsFailure.Should().BeTrue();
        var mismatch = folderResult.FirstException.Should().BeOfType<StoredNameMismatchError>().Subject;
        mismatch.Resource.Should().Be(new ResourceKey("incoming"));
        mismatch.StoredResource.Should().Be(new ResourceKey("Incoming"));
    }

    [Test]
    public void AFileAtThePath_LeavesDownloadsInTheDefaultFolder()
    {
        File.WriteAllText(ResolvePathOnDisk("incoming"), "a file, not a folder");

        var folderResult = DownloadsFolderPath.Resolve(_registry, _fileSystem, "incoming");

        folderResult.IsSuccess.Should().BeTrue();
        folderResult.Value.Should().Be(DownloadsFolderPath.DefaultFolder);
    }

    [TestCase("", Description = "no folder named")]
    [TestCase("notes.txt", Description = "a file rather than a folder")]
    [TestCase("../outside", Description = "a path that names no folder")]
    [TestCase(".git", Description = "a folder Celbridge reserves")]
    public void AnythingElse_LeavesDownloadsInTheDefaultFolder(string path)
    {
        File.WriteAllText(ResolvePathOnDisk("notes.txt"), "a file, not a folder");

        var folderResult = DownloadsFolderPath.Resolve(_registry, _fileSystem, path);

        folderResult.IsSuccess.Should().BeTrue();
        folderResult.Value.Should().Be(DownloadsFolderPath.DefaultFolder);
        folderResult.Value.Should().Be(new ResourceKey("downloads"));
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = "On a case-sensitive file system, Downloads and downloads are different folders.")]
    public void TheDefaultFolderInAnotherCase_IsRefused()
    {
        Directory.CreateDirectory(ResolvePathOnDisk("Downloads"));

        var folderResult = DownloadsFolderPath.Resolve(_registry, _fileSystem, string.Empty);

        folderResult.IsFailure.Should().BeTrue();
        folderResult.HasException<StoredNameMismatchError>().Should().BeTrue();
        folderResult.MessageChain.Should().Contain("Canonical form is 'project:Downloads'");
    }

    private string ResolvePathOnDisk(string path)
    {
        return Path.Combine(_projectFolderPath, path.Replace('/', Path.DirectorySeparatorChar));
    }
}
