using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for ResourceScanner's project-wide sidecar enumeration: only paired
/// sidecars participate in tag queries.
/// </summary>
[TestFixture]
public class ResourceScannerTests
{
    private IResourceFileSystem _resourceFileSystem = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private ResourceScanner _scanner = null!;

    [SetUp]
    public void Setup()
    {
        _resourceFileSystem = Substitute.For<IResourceFileSystem>();
        _resourceRegistry = Substitute.For<IResourceRegistry>();

        // Default: nothing exists on disk. Tests opt-in per resource.
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.NotFound, 0, default, FileSystemAttributes.None))));

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.FileSystem.Returns(_resourceFileSystem);
        workspaceService.ResourceService.Registry.Returns(_resourceRegistry);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var logger = Celbridge.Tests.Migration.TestHelpers.MigrationTestHelper.CreateMockLogger<ResourceScanner>();
        _scanner = new ResourceScanner(logger, workspaceWrapper);
    }

    [Test]
    public async Task FindByTagAsync_FindsTagInSiblingSidecar_WhenParentExists()
    {
        // Baseline: a paired sidecar's tag surfaces as the parent's key, not
        // the sidecar's key — agents never see .cel keys in tag results.
        var parentKey = new ResourceKey("photo.png");
        var sidecarKey = new ResourceKey("photo.png.cel");
        var sidecarPath = @"C:\Project\photo.png.cel";

        _resourceRegistry.GetAllFileResources(ResourceKey.DefaultRoot)
            .Returns(new List<FileResourceEntry> { new(sidecarKey, sidecarPath) });

        _resourceFileSystem.GetInfoAsync(parentKey)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(sidecarKey)
            .Returns(Task.FromResult(Result<string>.Ok("_tags = [\"boring\"]\n")));

        var results = await _scanner.FindByTagAsync("boring");

        results.Should().ContainSingle()
            .Which.Should().Be(parentKey);
    }

    [Test]
    public async Task FindByTagAsync_SkipsOrphanCel_EvenIfTagBytesPresent()
    {
        // Parentless .cel files are orphans; they don't participate in tag
        // queries because they are not legitimate resources from the user's
        // point of view. The project-check reporter surfaces them separately.
        var orphanKey = new ResourceKey("leftover.cel");
        var orphanPath = @"C:\Project\leftover.cel";

        _resourceRegistry.GetAllFileResources(ResourceKey.DefaultRoot)
            .Returns(new List<FileResourceEntry> { new(orphanKey, orphanPath) });

        // The would-be parent (leftover) does not exist on disk (default
        // NotFound from setup), so the scanner skips this file.
        _resourceFileSystem.ReadAllTextAsync(orphanKey)
            .Returns(Task.FromResult(Result<string>.Ok("_tags = [\"boring\"]\n")));

        var results = await _scanner.FindByTagAsync("boring");

        results.Should().BeEmpty();
    }
}
