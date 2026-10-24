using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Models;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;
using Celbridge.Messaging;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests the three outcomes of ResourceOperationService.GetWritableStateAsync:
/// writable, OS read-only attribute, and read-only root. The query is the single
/// source of truth for the editor and at-rest dimming surfaces, so each source
/// needs its own coverage.
/// </summary>
[TestFixture]
public class GetWritableStateTests
{
    private IResourceFileSystem _resourceFileSystem = null!;
    private IRootHandlerRegistry _rootHandlerRegistry = null!;
    private IResourceService _resourceService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _resourceFileSystem = Substitute.For<IResourceFileSystem>();

        // Default file probe: an existing regular file with no read-only bit.
        // Per-test overrides reset this for the ReadOnlyAttribute case.
        var writableFileInfo = new StorageItemInfo(StorageItemKind.File, 0, DateTime.UtcNow, FileSystemAttributes.None);
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Result<StorageItemInfo>.Ok(writableFileInfo));

        _rootHandlerRegistry = Substitute.For<IRootHandlerRegistry>();
        _rootHandlerRegistry.RootHandlers.Returns(new Dictionary<string, IResourceRootHandler>());

        _resourceService = Substitute.For<IResourceService>();
        _resourceService.FileSystem.Returns(_resourceFileSystem);
        _resourceService.RootHandlers.Returns(_rootHandlerRegistry);
        _resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(_resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
        _workspaceWrapper.IsWorkspaceLoaded.Returns(false);
    }

    [Test]
    public async Task ReturnsWritable_ForRegularFile()
    {
        var operationService = CreateOperationService();

        var state = await operationService.GetWritableStateAsync(new ResourceKey("notes/todo.md"));

        state.Should().Be(WritableState.Writable);
    }

    [Test]
    public async Task ReturnsReadOnlyRoot_ForFileOnNonWritableRoot()
    {
        var bundledHandler = Substitute.For<IResourceRootHandler>();
        bundledHandler.Capabilities.Returns(new ResourceRootCapabilities(IsWritable: false, IsWatched: false, ListingPolicy.None));
        _rootHandlerRegistry.RootHandlers.Returns(new Dictionary<string, IResourceRootHandler>
        {
            ["bundled"] = bundledHandler,
        });
        var operationService = CreateOperationService();

        var state = await operationService.GetWritableStateAsync(new ResourceKey("bundled:docs/readme.md"));

        state.Should().Be(WritableState.ReadOnlyRoot);
    }

    [Test]
    public async Task ReturnsReadOnlyAttribute_WhenFileCarriesReadOnlyBit()
    {
        var readOnlyFileInfo = new StorageItemInfo(StorageItemKind.File, 0, DateTime.UtcNow, FileSystemAttributes.ReadOnly);
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Result<StorageItemInfo>.Ok(readOnlyFileInfo));
        var operationService = CreateOperationService();

        var state = await operationService.GetWritableStateAsync(new ResourceKey("notes/todo.md"));

        state.Should().Be(WritableState.ReadOnlyAttribute);
    }

    [Test]
    public async Task ReadOnlyRootTakesPriority_OverReadOnlyAttribute()
    {
        // A read-only-root file with the OS read-only bit also set reports
        // ReadOnlyRoot. The structural source names the more durable cause
        // (bundled root never becomes writable; the attribute can be cleared).
        var bundledHandler = Substitute.For<IResourceRootHandler>();
        bundledHandler.Capabilities.Returns(new ResourceRootCapabilities(IsWritable: false, IsWatched: false, ListingPolicy.None));
        _rootHandlerRegistry.RootHandlers.Returns(new Dictionary<string, IResourceRootHandler>
        {
            ["bundled"] = bundledHandler,
        });

        var readOnlyFileInfo = new StorageItemInfo(StorageItemKind.File, 0, DateTime.UtcNow, FileSystemAttributes.ReadOnly);
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Result<StorageItemInfo>.Ok(readOnlyFileInfo));

        var operationService = CreateOperationService();

        var state = await operationService.GetWritableStateAsync(new ResourceKey("bundled:docs/readme.md"));

        state.Should().Be(WritableState.ReadOnlyRoot);
    }

    private ResourceOperationService CreateOperationService()
    {
        return new ResourceOperationService(
            Substitute.For<ILogger<ResourceOperationService>>(),
            _workspaceWrapper,
            Substitute.For<IProjectService>(),
            TestFileSystem.CreateLocal());
    }
}
