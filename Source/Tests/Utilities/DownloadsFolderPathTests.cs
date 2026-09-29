using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Utilities;

/// <summary>
/// A project names the folder its downloads are saved to by a path from the project root. These tests pin
/// which paths name a folder at all, that a folder Celbridge reserves is not one, that a named folder is
/// used whether or not the project has made it yet, and that a path the project cannot use saves downloads
/// to the default rather than failing them.
/// </summary>
[TestFixture]
public class DownloadsFolderPathTests
{
    private IResourceRegistry _registry = null!;
    private ILocalFileSystem _fileSystem = null!;
    private Dictionary<string, IResource> _projectResources = null!;
    private string _projectFolderPath = null!;

    [SetUp]
    public void Setup()
    {
        // The project's resources by path. The registry finds one whatever the case of the key it is given.
        _projectResources = new Dictionary<string, IResource>(StringComparer.OrdinalIgnoreCase);

        // The project on disk, which can hold an item the registry has not picked up yet.
        _projectFolderPath = Path.Combine(Path.GetTempPath(), $"DownloadsFolderPathTests_{Guid.NewGuid():N}");
        _fileSystem = TestFileSystem.CreateLocal();
        Directory.CreateDirectory(_projectFolderPath);

        _registry = Substitute.For<IResourceRegistry>();
        _registry.NormalizeResourceKey(Arg.Any<ResourceKey>())
            .Returns(callInfo => NormalizeResource(callInfo.Arg<ResourceKey>()));
        _registry.GetResource(Arg.Any<ResourceKey>())
            .Returns(callInfo => GetResource(callInfo.Arg<ResourceKey>()));
        _registry.ResolveResourcePath(Arg.Any<ResourceKey>(), Arg.Any<bool>())
            .Returns(callInfo => Result<string>.Ok(ResolvePathOnDisk(callInfo.Arg<ResourceKey>())));
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
    [TestCase(".celbridge")]
    [TestCase(".git/downloads")]
    [TestCase("assets/.git")]
    [TestCase("vendor/.celbridge/temp")]
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
    [TestCase("assets/.celbridge", true)]
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
        AddFolder("assets/incoming");

        var folder = DownloadsFolderPath.Resolve(_registry, _fileSystem, "assets/incoming");

        folder.Should().Be(new ResourceKey("assets/incoming"));
    }

    [Test]
    public void AFolderTheProjectHasNotMade_IsWhereDownloadsGo()
    {
        var folder = DownloadsFolderPath.Resolve(_registry, _fileSystem, "assets/incoming");

        folder.Should().Be(new ResourceKey("assets/incoming"));
    }

    [Test]
    public void AFolderNamedInAnotherCase_IsTakenAsTheProjectSpellsIt()
    {
        AddFolder("Incoming");

        var folder = DownloadsFolderPath.Resolve(_registry, _fileSystem, "incoming");

        folder.Should().Be(new ResourceKey("Incoming"));
    }

    [Test]
    public void AFolderOnDiskTheRegistryHasNotPickedUp_IsWhereDownloadsGo()
    {
        // WebView2 creates the folder for the first download it saves there, and asks where that download
        // goes before the registry has seen the folder.
        Directory.CreateDirectory(ResolvePathOnDisk(new ResourceKey("Incoming")));

        var folder = DownloadsFolderPath.Resolve(_registry, _fileSystem, "incoming");

        folder.Should().Be(new ResourceKey("Incoming"));
    }

    [Test]
    public void AFileOnDiskTheRegistryHasNotPickedUp_LeavesDownloadsInTheDefaultFolder()
    {
        File.WriteAllText(ResolvePathOnDisk(new ResourceKey("incoming")), "a file, not a folder");

        var folder = DownloadsFolderPath.Resolve(_registry, _fileSystem, "incoming");

        folder.Should().Be(DownloadsFolderPath.DefaultFolder);
    }

    [TestCase("", Description = "no folder named")]
    [TestCase("notes.txt", Description = "a file rather than a folder")]
    [TestCase("../outside", Description = "a path that names no folder")]
    [TestCase(".git", Description = "a folder Celbridge reserves")]
    public void AnythingElse_LeavesDownloadsInTheDefaultFolder(string path)
    {
        _projectResources["notes.txt"] = Substitute.For<IFileResource>();

        var folder = DownloadsFolderPath.Resolve(_registry, _fileSystem, path);

        folder.Should().Be(DownloadsFolderPath.DefaultFolder);
        folder.Should().Be(new ResourceKey("downloads"));
    }

    [Test]
    public void TheDefaultFolder_IsTakenAsTheProjectSpellsIt()
    {
        AddFolder("Downloads");

        var folder = DownloadsFolderPath.Resolve(_registry, _fileSystem, string.Empty);

        folder.Should().Be(new ResourceKey("Downloads"));
    }

    private void AddFolder(string path)
    {
        _projectResources[path] = Substitute.For<IFolderResource>();
    }

    // Finds the resource in the registry, or failing that on disk, as the project spells it.
    private Result<ResourceKey> NormalizeResource(ResourceKey resource)
    {
        var registeredPath = _projectResources.Keys.FirstOrDefault(path =>
            string.Equals(path, resource.Path, StringComparison.OrdinalIgnoreCase));
        if (registeredPath is not null)
        {
            return new ResourceKey(registeredPath);
        }

        var pathOnDisk = Directory.EnumerateFileSystemEntries(_projectFolderPath)
            .Select(Path.GetFileName)
            .FirstOrDefault(name => string.Equals(name, resource.Path, StringComparison.OrdinalIgnoreCase));
        if (pathOnDisk is null)
        {
            return Result<ResourceKey>.Fail($"'{resource}' does not exist");
        }

        return new ResourceKey(pathOnDisk);
    }

    private string ResolvePathOnDisk(ResourceKey resource)
    {
        return Path.Combine(_projectFolderPath, resource.Path);
    }

    // Looked up by the key as given, as the registry does once a key is normalized.
    private Result<IResource> GetResource(ResourceKey resource)
    {
        var entry = _projectResources.FirstOrDefault(pair =>
            string.Equals(pair.Key, resource.Path, StringComparison.Ordinal));
        if (entry.Value is null)
        {
            return Result<IResource>.Fail($"'{resource}' does not exist");
        }

        return Result<IResource>.Ok(entry.Value);
    }
}
