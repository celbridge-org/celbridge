using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Projects.Services;
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
    private IResourceService _resourceService = null!;
    private LocalResourceFileSystem _resourceFileSystem = null!;
    private TrashService _trashService = null!;
    private ResourceOperationService _operationService = null!;
    private IProjectService _projectService = null!;

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
        var referenceIndex = ResourceReferenceIndexTestHelper.Empty();
        resourceScanner.BuildReferenceIndexAsync()
            .Returns(Task.FromResult(referenceIndex));

        var rootHandlerRegistry = Substitute.For<IRootHandlerRegistry>();
        rootHandlerRegistry.RootHandlers.Returns(new Dictionary<string, IResourceRootHandler>());

        var resourceService = Substitute.For<IResourceService>();
        _resourceService = resourceService;
        resourceService.Registry.Returns(_resourceRegistry);
        resourceService.RootHandlers.Returns(rootHandlerRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Scanner.Returns(resourceScanner);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
        _workspaceWrapper.IsWorkspaceLoaded.Returns(false);

        var sidecarService = new SidecarService(_workspaceWrapper);
        resourceService.Sidecars.Returns(sidecarService);

        _resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(_resourceFileSystem);

        _projectService = Substitute.For<IProjectService>();
        StubCurrentProject("Acme.celbridge");

        _trashService = new TrashService(
            Substitute.For<ILogger<TrashService>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper,
            _projectService,
            TestFileSystem.CreateLocal());
        resourceService.Trash.Returns(_trashService);

        _operationService = new ResourceOperationService(
            Substitute.For<ILogger<ResourceOperationService>>(),
            _workspaceWrapper,
            _projectService,
            TestFileSystem.CreateLocal());
    }

    [Test]
    public async Task MovingTheProjectFileOutOfItsFolder_IsRefused()
    {
        StubCurrentProject("Acme.celbridge");

        var result = await _operationService.MoveAsync(
            new ResourceKey("Acme.celbridge"),
            new ResourceKey("sub/Acme.celbridge"));

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("cannot be moved out of it");
    }

    [Test]
    public async Task RenamingTheProjectFileInPlace_IsNotRefused()
    {
        // The guard lets the rename through, so the move then fails on the missing source. Asserting on
        // which failure came back is what distinguishes "allowed" from "refused" without staging files.
        StubCurrentProject("Acme.celbridge");

        var result = await _operationService.MoveAsync(
            new ResourceKey("Acme.celbridge"),
            new ResourceKey("Renamed.celbridge"));

        result.FirstErrorMessage.Should().NotContain("Move the whole folder instead");
    }

    [Test]
    public async Task MovingAnOrdinaryFile_IsNotRefused()
    {
        StubCurrentProject("Acme.celbridge");

        var result = await _operationService.MoveAsync(
            new ResourceKey("notes.md"),
            new ResourceKey("sub/notes.md"));

        result.FirstErrorMessage.Should().NotContain("Move the whole folder instead");
    }

    [Test]
    public async Task RenamingTheProjectFileToAnotherExtension_IsRefused()
    {
        StubCurrentProject("Acme.celbridge");

        var result = await _operationService.MoveAsync(
            new ResourceKey("Acme.celbridge"),
            new ResourceKey("Acme.txt"));

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("must keep its .celbridge extension");
    }

    // A real Project rather than a substitute, so the guard runs the actual IsProjectFile comparison
    // instead of a stub's default answer.
    private void StubCurrentProject(string projectFileName)
    {
        var projectFolderPath = _tempFolder;

        var project = new Project(
            Path.Combine(projectFolderPath, projectFileName),
            Path.GetFileNameWithoutExtension(projectFileName),
            projectFolderPath,
            new ProjectConfig(),
            MigrationResult.Success(),
            ConfigIsHealthy: true,
            ConfigLoadFailure: null);

        _projectService.CurrentProject.Returns(project);
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
    public async Task DeleteAsync_DeniesByPolicy_WhenResourceIsReserved()
    {
        // The trash-based delete bypasses IResourceFileSystem.DeleteAsync, so this
        // guard at the service entry is the load-bearing one for the reserved folders.
        var metadataFolder = Path.Combine(_tempFolder, ".celbridge");
        Directory.CreateDirectory(metadataFolder);

        var deleteResult = await _operationService.DeleteAsync(new ResourceKey(".celbridge"));

        deleteResult.IsFailure.Should().BeTrue();
        deleteResult.HasException<PolicyDenialError>().Should().BeTrue();
        Directory.Exists(metadataFolder).Should().BeTrue();
    }

    [Test]
    public async Task CreateFileAsync_DeniesByPolicy_WhenDestinationIsReserved()
    {
        var result = await _operationService.CreateFileAsync(
            new ResourceKey(".celbridge/state.json"), new byte[] { 0x01 });

        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, ".celbridge", "state.json")).Should().BeFalse();
    }

    [Test]
    public async Task CreateFileAsync_Succeeds_WhenDestinationIsHidden()
    {
        // Hiding is cosmetic: a destination the Explorer would not draw is still a
        // destination anything may write to.
        var section = new ResourcesSection
        {
            Hide = new[] { "*.log" },
        };
        // Built into a local first: BuildPolicy stands up its own substitutes, which would
        // corrupt NSubstitute's last-call context if it ran inside Returns(...).
        var policy = BuildPolicy(section);
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var result = await _operationService.CreateFileAsync(new ResourceKey("notes.log"), new byte[] { 0x01 });

        result.IsSuccess.Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "notes.log")).Should().BeTrue();
    }

    [Test]
    public async Task MoveAsync_DeniesByPolicy_WhenSourceIsReserved()
    {
        var metadataFolder = Path.Combine(_tempFolder, ".celbridge");
        Directory.CreateDirectory(metadataFolder);

        var result = await _operationService.MoveAsync(new ResourceKey(".celbridge"), new ResourceKey("Archive"));

        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();
        Directory.Exists(Path.Combine(_tempFolder, "Archive")).Should().BeFalse();
    }

    [Test]
    public void CanCreateResource_Fails_WhenDestinationIsReserved()
    {
        var result = _operationService.CanCreateResource(new ResourceKey(".git/config"), isFolder: false);

        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();
    }

    [Test]
    public void CanAddToFolder_Fails_WhenFolderIsReserved()
    {
        var result = _operationService.CanAddToFolder(new ResourceKey(".celbridge"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void CanAddToFolder_Succeeds_WhenFolderIsSearchExcluded()
    {
        // Scan scope binds the indexers, not the operation layer.
        var section = new ResourcesSection
        {
            SearchExclude = new[] { "node_modules" },
        };
        // Built into a local first: BuildPolicy stands up its own substitutes, which would
        // corrupt NSubstitute's last-call context if it ran inside Returns(...).
        var policy = BuildPolicy(section);
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var result = _operationService.CanAddToFolder(new ResourceKey("node_modules"));

        result.IsSuccess.Should().BeTrue();
    }

    private static ResourcePolicy BuildPolicy(ResourcesSection section)
    {
        var config = new ProjectConfig { Resources = section };
        var project = Substitute.For<IProject>();
        project.Config.Returns(config);
        project.ProjectFolderPath.Returns(@"C:\fake\project");

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        return new ResourcePolicy(projectService);
    }
}
