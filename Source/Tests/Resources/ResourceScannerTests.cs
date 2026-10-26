using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for ResourceScanner's project-wide sidecar walk over a temporary project folder: only paired sidecars
/// participate in tag queries.
/// </summary>
[TestFixture]
public class ResourceScannerTests
{
    private string _projectFolderPath = null!;
    private IResourceService _resourceService = null!;
    private ResourceScanner _scanner = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ResourceScannerTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        var messengerService = new MessengerService();

        var resourceService = Substitute.For<IResourceService>();
        resourceService.RootHandlers.Returns(TestRootHandlers.Create(_projectFolderPath));
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());
        _resourceService = resourceService;

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            messengerService,
            workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);

        var walker = new ResourceWalker(Substitute.For<ILogger<ResourceWalker>>(), workspaceWrapper);
        resourceService.Walker.Returns(walker);
        resourceService.Sidecars.Returns(new SidecarService(workspaceWrapper));

        _scanner = new ResourceScanner(Substitute.For<ILogger<ResourceScanner>>(), workspaceWrapper);
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
    public async Task FindByTagAsync_FindsTagInSiblingSidecar_WhenParentExists()
    {
        // Baseline: a paired sidecar's tag surfaces as the parent's key, not
        // the sidecar's key — agents never see .cel keys in tag results.
        WriteFile("images/photo.png", "png");
        WriteFile("images/photo.png.cel", "_tags = [\"boring\"]\n");

        var results = await _scanner.FindByTagAsync("boring", applySearchExclude: true);

        results.Should().ContainSingle()
            .Which.Should().Be(new ResourceKey("images/photo.png"));
    }

    [Test]
    public async Task FindByTagAsync_SkipsOrphanCel_EvenIfTagBytesPresent()
    {
        // Parentless .cel files are orphans; they don't participate in tag
        // queries because they are not legitimate resources from the user's
        // point of view. data_inspect lists them separately.
        WriteFile("leftover.cel", "_tags = [\"boring\"]\n");

        var results = await _scanner.FindByTagAsync("boring", applySearchExclude: true);

        results.Should().BeEmpty();
    }

    [Test]
    public async Task FindByTagAsync_LeavesOutWhatTheSearchListExcludes_UnlessAskedToIncludeIt()
    {
        WriteFile("dist/photo.png", "png");
        WriteFile("dist/photo.png.cel", "_tags = [\"boring\"]\n");
        UseSearchExclude("dist");

        var defaultResults = await _scanner.FindByTagAsync("boring", applySearchExclude: true);
        var includedResults = await _scanner.FindByTagAsync("boring", applySearchExclude: false);

        defaultResults.Should().BeEmpty();
        includedResults.Should().Equal(new ResourceKey("dist/photo.png"));
    }

    [Test]
    public async Task ListAllTagsAsync_LeavesOutWhatTheSearchListExcludes_UnlessAskedToIncludeIt()
    {
        WriteFile("notes.md", "notes");
        WriteFile("notes.md.cel", "_tags = [\"kept\"]\n");
        WriteFile("dist/photo.png", "png");
        WriteFile("dist/photo.png.cel", "_tags = [\"built\"]\n");
        UseSearchExclude("dist");

        var defaultTags = await _scanner.ListAllTagsAsync(applySearchExclude: true);
        var includedTags = await _scanner.ListAllTagsAsync(applySearchExclude: false);

        defaultTags.Should().Equal("kept");
        includedTags.Should().Equal("built", "kept");
    }

    private void UseSearchExclude(params string[] patterns)
    {
        var policy = TestResourcePolicy.CreateWithSearchExclude(_projectFolderPath, patterns);
        _resourceService.Policy.Returns(policy);
    }

    private void WriteFile(string relativePath, string content)
    {
        var filePath = Path.Combine(_projectFolderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content);
    }
}
