using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for ResourceOperationService — covers the batch property that a
/// batch failing mid-way still commits the prior-successful operations,
/// and a single UndoAsync reverses them cleanly.
/// </summary>
[TestFixture]
public class ResourceOperationServiceTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private FileStorage _fileStorage = null!;
    private TrashService _trashService = null!;
    private ResourceOperationService _operationService = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ResourceOperationServiceTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_tempFolder);
        _resourceRegistry.RootHandlers.Returns(new Dictionary<string, IResourceRootHandler>());

        // Map every key under the default root to a path under the temp folder.
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>()).Returns(call =>
        {
            var key = call.Arg<ResourceKey>();
            if (key.IsEmpty)
            {
                return Result<string>.Ok(_tempFolder);
            }
            var relativePath = key.Path.Replace('/', Path.DirectorySeparatorChar);
            return Result<string>.Ok(Path.Combine(_tempFolder, relativePath));
        });

        // Inverse mapping (used by FileStorage's descendant-key enumeration on
        // folder moves and deletes).
        _resourceRegistry.GetResourceKey(Arg.Any<string>()).Returns(call =>
        {
            var fullPath = call.Arg<string>();
            var relativePart = Path.GetRelativePath(_tempFolder, fullPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            return Result<ResourceKey>.Ok(new ResourceKey(relativePart));
        });

        var resourceScanner = Substitute.For<IResourceScanner>();
        resourceScanner.FindReferencersAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult<IReadOnlyList<ResourceKey>>(Array.Empty<ResourceKey>()));
        resourceScanner.FindAllReferencedTargetsAsync()
            .Returns(Task.FromResult<IReadOnlyList<ResourceKey>>(Array.Empty<ResourceKey>()));

        var rootHandlerRegistry = Substitute.For<IRootHandlerRegistry>();
        rootHandlerRegistry.RootHandlers.Returns(new Dictionary<string, IResourceRootHandler>());

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);
        resourceService.RootHandlerRegistry.Returns(rootHandlerRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        workspaceService.ResourceScanner.Returns(resourceScanner);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
        // IsWorkspacePageLoaded = false skips the entity-data cascade so the
        // tests don't need to wire an IEntityService.
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        var sidecarService = new SidecarService(_workspaceWrapper);
        workspaceService.SidecarService.Returns(sidecarService);

        _fileStorage = new FileStorage(
            Substitute.For<ILogger<FileStorage>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper);
        workspaceService.FileStorage.Returns(_fileStorage);

        _trashService = new TrashService(
            Substitute.For<ILogger<TrashService>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper);
        workspaceService.TrashService.Returns(_trashService);

        _operationService = new ResourceOperationService(
            Substitute.For<ILogger<ResourceOperationService>>(),
            _workspaceWrapper);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    [Test]
    public async Task PartialBatch_FailureMidway_CommitsPriorOps_AndSingleUndoReversesThem()
    {
        // Pre-create a file outside the batch so the second CreateFileAsync
        // inside the batch fails with "Resource already exists".
        var existingPath = Path.Combine(_tempFolder, "existing.txt");
        await File.WriteAllTextAsync(existingPath, "preexisting");

        var newResource = new ResourceKey("new.txt");
        var existingResource = new ResourceKey("existing.txt");
        var newPath = Path.Combine(_tempFolder, "new.txt");

        using (var batch = _operationService.BeginBatch())
        {
            var firstCreate = await _operationService.CreateFileAsync(newResource, new byte[] { 0x01, 0x02 });
            firstCreate.IsSuccess.Should().BeTrue();

            // The second op fails inside CreateOperation.ExecuteAsync (the
            // probe sees the existing file) and is NOT added to the batch.
            var secondCreate = await _operationService.CreateFileAsync(existingResource, new byte[] { 0xFF });
            secondCreate.IsFailure.Should().BeTrue();
        }

        // After the using-block commits the partial batch: the newly-created
        // file is on disk and the pre-existing file is untouched.
        File.Exists(newPath).Should().BeTrue();
        File.Exists(existingPath).Should().BeTrue();
        (await File.ReadAllTextAsync(existingPath)).Should().Be("preexisting");

        // A single UndoAsync reverses the committed partial batch: the new
        // file is deleted; the pre-existing file (never inside the batch) stays.
        _operationService.CanUndo.Should().BeTrue();
        var undoResult = await _operationService.UndoAsync();
        undoResult.IsSuccess.Should().BeTrue();

        File.Exists(newPath).Should().BeFalse();
        File.Exists(existingPath).Should().BeTrue();
        _operationService.CanUndo.Should().BeFalse();
        _operationService.CanRedo.Should().BeTrue();
    }

    [Test]
    public async Task EmptyBatch_DoesNotPushAnUndoEntry()
    {
        using (var batch = _operationService.BeginBatch())
        {
            // No operations queued.
        }

        // An empty batch is discarded — CanUndo stays false.
        _operationService.CanUndo.Should().BeFalse();
    }

    [Test]
    public async Task BatchScope_CommitsOnDispose_EvenWhenReturnExitsEarly()
    {
        var firstResource = new ResourceKey("a.txt");
        var secondResource = new ResourceKey("b.txt");

        // Wrap in a local async function that returns early from inside the
        // using block; the BatchScope's Dispose must still commit on the way out.
        async Task<bool> RunPartialBatch()
        {
            using var batch = _operationService.BeginBatch();
            var first = await _operationService.CreateFileAsync(firstResource, new byte[] { 0x01 });
            if (first.IsFailure)
            {
                return false;
            }

            // Early return mid-batch — the second CreateFileAsync never runs.
            return true;
        }

        var ran = await RunPartialBatch();
        ran.Should().BeTrue();

        File.Exists(Path.Combine(_tempFolder, "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "b.txt")).Should().BeFalse();

        // The partially-populated batch did commit — UndoAsync reverses the
        // single create.
        _operationService.CanUndo.Should().BeTrue();
        var undoResult = await _operationService.UndoAsync();
        undoResult.IsSuccess.Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "a.txt")).Should().BeFalse();
    }
}
