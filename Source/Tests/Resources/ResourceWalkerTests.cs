using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for ResourceWalker over a temporary project folder, through real root handlers and a real gateway.
/// </summary>
[TestFixture]
public class ResourceWalkerTests
{
    private string _projectFolderPath = null!;
    private IResourceService _resourceService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private ResourceWalker _walker = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ResourceWalkerTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        var messengerService = new MessengerService();

        _resourceService = Substitute.For<IResourceService>();
        _resourceService.RootHandlers.Returns(TestRootHandlers.Create(_projectFolderPath));
        _resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(_resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            messengerService,
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        _resourceService.FileSystem.Returns(resourceFileSystem);

        _walker = new ResourceWalker(Substitute.For<ILogger<ResourceWalker>>(), _workspaceWrapper);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            try
            {
                Directory.Delete(_projectFolderPath, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public async Task Walk_ReturnsEveryFileAndFolder_EachFolderBeforeItsContents()
    {
        WriteFile("root.txt");
        WriteFile("sub/child.md");
        WriteFile("sub/deeper/leaf.txt");

        var items = await WalkAsync(ResourceKey.Empty);

        items.Select(item => item.Resource.Path).Should().Equal(
            "sub",
            "sub/deeper",
            "sub/deeper/leaf.txt",
            "sub/child.md",
            "root.txt");

        var deeper = items.Single(item => item.Resource.Path == "sub/deeper");
        deeper.IsFolder.Should().BeTrue();
        deeper.Path.Should().Be(Path.Combine(_projectFolderPath, "sub", "deeper"));

        var leaf = items.Single(item => item.Resource.Path == "sub/deeper/leaf.txt");
        leaf.IsFolder.Should().BeFalse();
        leaf.Path.Should().Be(Path.Combine(_projectFolderPath, "sub", "deeper", "leaf.txt"));
        leaf.Size.Should().Be(new FileInfo(leaf.Path).Length);
    }

    [Test]
    public async Task Walk_FromAFolder_ReturnsOnlyWhatTheFolderContains()
    {
        WriteFile("src/app.py");
        WriteFile("src2/app.py");

        var items = await WalkAsync(new ResourceKey("src"));

        items.Select(item => item.Resource.Path).Should().Equal("src/app.py");
    }

    [Test]
    public async Task Walk_FromAFolderInTheWrongCase_FailsAndGivesTheStoredName()
    {
        WriteFile("Src/App.py");
        if (!Directory.Exists(Path.Combine(_projectFolderPath, "src")))
        {
            Assert.Ignore("The volume compares names by case, so a key in the wrong case names nothing.");
        }

        var walkResult = await _walker.WalkAsync(new ResourceKey("src"));

        walkResult.IsFailure.Should().BeTrue();
        walkResult.HasException<StoredNameMismatchError>().Should().BeTrue();
        walkResult.MessageChain.Should().Contain("Canonical form is 'project:Src'");
    }

    [Test]
    public async Task Walk_LeavesOutWhatSearchExcludeMatches()
    {
        WriteFile("build/output/app.dll");
        WriteFile("secret.txt");
        WriteFile("src/app.py");
        UseSearchExclude("build/", "secret.txt");

        var items = await WalkAsync(ResourceKey.Empty);

        items.Select(item => item.Resource.Path).Should().Equal("src", "src/app.py");
    }

    [Test]
    public async Task Walk_WithSearchExcludeSwitchedOff_IncludesWhatItMatches()
    {
        WriteFile("build/output/app.dll");
        WriteFile("src/app.py");
        UseSearchExclude("build/");

        var items = await WalkAsync(ResourceKey.Empty, applySearchExclude: false);

        items.Select(item => item.Resource.Path).Should().Equal(
            "build",
            "build/output",
            "build/output/app.dll",
            "src",
            "src/app.py");
    }

    [Test]
    public async Task Walk_LeavesOutLinksAndGit()
    {
        WriteFile("target/inside.txt");
        WriteFile("target.txt");
        WriteFile(".git/config");
        Directory.CreateSymbolicLink(Path.Combine(_projectFolderPath, "linked"), Path.Combine(_projectFolderPath, "target"));
        File.CreateSymbolicLink(Path.Combine(_projectFolderPath, "linked.txt"), Path.Combine(_projectFolderPath, "target.txt"));

        var items = await WalkAsync(ResourceKey.Empty);

        items.Select(item => item.Resource.Path).Should().Equal("target", "target/inside.txt", "target.txt");
    }

    [Test]
    public async Task Walk_OfAFile_Fails()
    {
        WriteFile("notes.txt");

        var walkResult = await _walker.WalkAsync(new ResourceKey("notes.txt"));

        walkResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task Walk_ReturnsBeforeTheListingFinishes()
    {
        // The gateway lists a folder synchronously. This listing blocks until the test releases it.
        using var listingReleased = new ManualResetEventSlim();
        var blockingFileSystem = Substitute.For<IResourceFileSystem>();
        blockingFileSystem.EnumerateFolderAsync(Arg.Any<ResourceKey>()).Returns(_ =>
        {
            listingReleased.Wait(TimeSpan.FromSeconds(10));
            IReadOnlyList<FolderChild> noItems = Array.Empty<FolderChild>();
            return Task.FromResult(Result<IReadOnlyList<FolderChild>>.Ok(noItems));
        });
        _resourceService.FileSystem.Returns(blockingFileSystem);

        var walkTask = _walker.WalkAsync(ResourceKey.Empty);

        walkTask.IsCompleted.Should().BeFalse();
        listingReleased.Set();
        var walkResult = await walkTask;
        walkResult.IsSuccess.Should().BeTrue();
    }

    private async Task<IReadOnlyList<WalkedItem>> WalkAsync(ResourceKey folder, bool applySearchExclude = true)
    {
        var walkResult = await _walker.WalkAsync(folder, applySearchExclude);
        walkResult.IsSuccess.Should().BeTrue(walkResult.FirstErrorMessage);

        return walkResult.Value;
    }

    private void UseSearchExclude(params string[] patterns)
    {
        var policy = TestResourcePolicy.CreateWithSearchExclude(_projectFolderPath, patterns);
        _resourceService.Policy.Returns(policy);
    }

    private void WriteFile(string relativePath)
    {
        var filePath = Path.Combine(_projectFolderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "x");
    }
}
