using Celbridge.Entities;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
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
    private LocalResourceFileSystem _resourceFileSystem = null!;
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

        // Map every key under the default root to a path under the temp folder.
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>(), Arg.Any<bool>()).Returns(call =>
        {
            var key = call.Arg<ResourceKey>();
            if (key.IsEmpty)
            {
                return Result<string>.Ok(_tempFolder);
            }
            var relativePath = key.Path.Replace('/', Path.DirectorySeparatorChar);
            return Result<string>.Ok(Path.Combine(_tempFolder, relativePath));
        });

        // Inverse mapping (used by LocalResourceFileSystem's descendant-key enumeration on
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
        resourceService.RootHandlers.Returns(rootHandlerRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Scanner.Returns(resourceScanner);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
        // IsWorkspacePageLoaded = false skips the entity-data cascade so the
        // tests don't need to wire an IEntityService.
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        var sidecarService = new SidecarService(_workspaceWrapper);
        resourceService.Sidecars.Returns(sidecarService);

        _resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(_resourceFileSystem);

        _trashService = new TrashService(
            Substitute.For<ILogger<TrashService>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.Trash.Returns(_trashService);

        _operationService = new ResourceOperationService(
            Substitute.For<ILogger<ResourceOperationService>>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
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

    [Test]
    public async Task DeleteAsync_DeniesByPolicy_WhenFolderIsLocked()
    {
        // Stand up a real policy with the assets/ folder locked. The trash-based
        // delete bypasses IResourceFileSystem.DeleteAsync, so this guard at the
        // service entry is the load-bearing one.
        var section = new ResourcesSection
        {
            Lock = new[] { "assets" },
        };
        var policy = BuildPolicyForLocked(section);
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var assetsFolder = Path.Combine(_tempFolder, "assets");
        Directory.CreateDirectory(assetsFolder);
        await File.WriteAllTextAsync(Path.Combine(assetsFolder, "logo.png"), "x");

        var deleteResult = await _operationService.DeleteAsync(new ResourceKey("assets"));

        deleteResult.IsFailure.Should().BeTrue();
        deleteResult.HasException<PolicyDenialError>().Should().BeTrue();
        Directory.Exists(assetsFolder).Should().BeTrue();
    }

    [Test]
    public async Task DeleteAsync_DeniesFolderDelete_WhenContentsAreLocked()
    {
        // locked = ["Data/**"] only matches files INSIDE Data, not the Data
        // folder key itself. Deleting Data must still fail because the cascade
        // would silently remove locked descendants.
        var section = new ResourcesSection
        {
            Lock = new[] { "Data/**" },
        };
        var policy = BuildPolicyForLocked(section);
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var dataFolder = Path.Combine(_tempFolder, "Data");
        Directory.CreateDirectory(dataFolder);
        await File.WriteAllTextAsync(Path.Combine(dataFolder, "REPORT.md"), "x");

        var deleteResult = await _operationService.DeleteAsync(new ResourceKey("Data"));

        deleteResult.IsFailure.Should().BeTrue();
        deleteResult.HasException<PolicyDenialError>().Should().BeTrue();
        Directory.Exists(dataFolder).Should().BeTrue();
        File.Exists(Path.Combine(dataFolder, "REPORT.md")).Should().BeTrue();
    }

    [Test]
    public async Task MoveAsync_DeniesFolderMove_WhenContentsAreLocked()
    {
        // Moving (or renaming) Data would relocate the locked Data/REPORT.md,
        // changing its path. The structural cascade must refuse the move so the
        // locked resource stays frozen in place.
        var section = new ResourcesSection
        {
            Lock = new[] { "Data/**" },
        };
        var policy = BuildPolicyForLocked(section);
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var dataFolder = Path.Combine(_tempFolder, "Data");
        Directory.CreateDirectory(dataFolder);
        await File.WriteAllTextAsync(Path.Combine(dataFolder, "REPORT.md"), "x");

        var moveResult = await _operationService.MoveAsync(new ResourceKey("Data"), new ResourceKey("Archive"));

        moveResult.IsFailure.Should().BeTrue();
        moveResult.HasException<PolicyDenialError>().Should().BeTrue();
        Directory.Exists(dataFolder).Should().BeTrue();
        Directory.Exists(Path.Combine(_tempFolder, "Archive")).Should().BeFalse();
    }

    [Test]
    public async Task CreateFileAsync_DeniesByPolicy_WhenDestinationIgnored()
    {
        // Part A destination-visibility gate: creating a resource the ignore-file
        // would immediately hide is refused rather than silently writing an
        // invisible file.
        var policy = BuildPolicyForIgnore("*.log\n");
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var result = await _operationService.CreateFileAsync(new ResourceKey("notes.log"), new byte[] { 0x01 });

        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "notes.log")).Should().BeFalse();
    }

    [Test]
    public async Task CreateFileAsync_Succeeds_WhenAddResurfacesIgnoredDestination()
    {
        // The .mcp.json-style granular add path still resolves: the ignore-file
        // hides every dotfile, but an add pattern brings the destination back
        // into the resource set, so the create is allowed.
        var section = new ResourcesSection
        {
            Add = new[] { ".mcp.json" },
        };
        var policy = BuildPolicyForIgnore(".*\n", section);
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var result = await _operationService.CreateFileAsync(new ResourceKey(".mcp.json"), new byte[] { 0x01 });

        result.IsSuccess.Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, ".mcp.json")).Should().BeTrue();
    }

    [Test]
    public async Task CreateFolderAsync_DeniesByPolicy_WhenDestinationIgnored()
    {
        var policy = BuildPolicyForIgnore("build/\n");
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var result = await _operationService.CreateFolderAsync(new ResourceKey("build"));

        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();
        Directory.Exists(Path.Combine(_tempFolder, "build")).Should().BeFalse();
    }

    [Test]
    public async Task MoveAsync_DeniesByPolicy_WhenDestinationIgnored()
    {
        var policy = BuildPolicyForIgnore("*.log\n");
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var sourcePath = Path.Combine(_tempFolder, "a.txt");
        await File.WriteAllTextAsync(sourcePath, "hello");

        var result = await _operationService.MoveAsync(new ResourceKey("a.txt"), new ResourceKey("a.log"));

        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();
        File.Exists(sourcePath).Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "a.log")).Should().BeFalse();
    }

    [Test]
    public async Task CopyAsync_DeniesByPolicy_WhenDestinationIgnored()
    {
        var policy = BuildPolicyForIgnore("*.log\n");
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var sourcePath = Path.Combine(_tempFolder, "a.txt");
        await File.WriteAllTextAsync(sourcePath, "hello");

        var result = await _operationService.CopyAsync(new ResourceKey("a.txt"), new ResourceKey("a.log"));

        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "a.log")).Should().BeFalse();
    }

    [Test]
    public async Task GetLockStateAsync_ReturnsLocked_WhenOwnKeyLocked()
    {
        var section = new ResourcesSection
        {
            Lock = new[] { "assets" },
        };
        var policy = BuildPolicyForLocked(section);
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var assetsFolder = Path.Combine(_tempFolder, "assets");
        Directory.CreateDirectory(assetsFolder);

        var state = await _operationService.GetLockStateAsync(new ResourceKey("assets"));

        state.Should().Be(ResourceLockState.Locked);
    }

    [Test]
    public async Task GetLockStateAsync_ReturnsContainsLocked_WhenDescendantLocked()
    {
        // lock = ["Data/**"] freezes descendants but not the Data key itself, so
        // the folder is path-frozen rather than fully locked.
        var section = new ResourcesSection
        {
            Lock = new[] { "Data/**" },
        };
        var policy = BuildPolicyForLocked(section);
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var dataFolder = Path.Combine(_tempFolder, "Data");
        Directory.CreateDirectory(dataFolder);
        await File.WriteAllTextAsync(Path.Combine(dataFolder, "REPORT.md"), "x");

        var state = await _operationService.GetLockStateAsync(new ResourceKey("Data"));

        state.Should().Be(ResourceLockState.ContainsLocked);
    }

    [Test]
    public async Task GetLockStateAsync_ReturnsNone_ForUnlockedFile()
    {
        var sourcePath = Path.Combine(_tempFolder, "notes.txt");
        await File.WriteAllTextAsync(sourcePath, "x");

        var state = await _operationService.GetLockStateAsync(new ResourceKey("notes.txt"));

        state.Should().Be(ResourceLockState.None);
    }

    [Test]
    public async Task CanModifyResourceAsync_Fails_WhenFolderContainsLockedDescendant()
    {
        var section = new ResourcesSection
        {
            Lock = new[] { "Data/**" },
        };
        var policy = BuildPolicyForLocked(section);
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var dataFolder = Path.Combine(_tempFolder, "Data");
        Directory.CreateDirectory(dataFolder);
        await File.WriteAllTextAsync(Path.Combine(dataFolder, "REPORT.md"), "x");

        var result = await _operationService.CanModifyResourceAsync(new ResourceKey("Data"));

        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();
    }

    [Test]
    public void CanCreateResource_Fails_WhenDestinationIgnored()
    {
        var policy = BuildPolicyForIgnore("*.log\n");
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var result = _operationService.CanCreateResource(new ResourceKey("notes.log"), isFolder: false);

        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();
    }

    [Test]
    public void CanCreateResource_Succeeds_WhenAddResurfacesDestination()
    {
        var section = new ResourcesSection
        {
            Add = new[] { ".mcp.json" },
        };
        var policy = BuildPolicyForIgnore(".*\n", section);
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var result = _operationService.CanCreateResource(new ResourceKey(".mcp.json"), isFolder: false);

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void CanAddToFolder_Fails_WhenFolderOwnKeyLocked()
    {
        var section = new ResourcesSection
        {
            Lock = new[] { "assets" },
        };
        var policy = BuildPolicyForLocked(section);
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var result = _operationService.CanAddToFolder(new ResourceKey("assets"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void CanAddToFolder_Succeeds_WhenOnlyDescendantsLocked()
    {
        // A pattern that locks only descendants (Data/**) leaves the folder
        // itself addable; new siblings are not frozen.
        var section = new ResourcesSection
        {
            Lock = new[] { "Data/**" },
        };
        var policy = BuildPolicyForLocked(section);
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var result = _operationService.CanAddToFolder(new ResourceKey("Data"));

        result.IsSuccess.Should().BeTrue();
    }

    private static ResourcePolicy BuildPolicyForLocked(ResourcesSection section)
    {
        // No ignore-file on disk, so the ignore baseline is empty and only the
        // lock list gates the write path under test.
        return BuildPolicy(section, ignoreFileContent: null);
    }

    private static ResourcePolicy BuildPolicyForIgnore(string ignoreFileContent, ResourcesSection? section = null)
    {
        return BuildPolicy(section ?? new ResourcesSection(), ignoreFileContent);
    }

    private static ResourcePolicy BuildPolicy(ResourcesSection section, string? ignoreFileContent)
    {
        var config = new ProjectConfig { Resources = section };
        var project = Substitute.For<IProject>();
        project.Config.Returns(config);
        project.ProjectFolderPath.Returns(@"C:\fake\project");
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        var fileSystem = Substitute.For<ILocalFileSystem>();
        var readResult = ignoreFileContent is null
            ? Result<string>.Fail("ignore-file not found")
            : Result<string>.Ok(ignoreFileContent);
        fileSystem.ReadAllTextAsync(Arg.Any<string>()).Returns(Task.FromResult(readResult));

        return new ResourcePolicy(projectService, fileSystem);
    }
}
