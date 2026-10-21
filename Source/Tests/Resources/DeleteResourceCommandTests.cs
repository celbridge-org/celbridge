using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Helpers;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Localization;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for DeleteResourceCommand over a real resource file system. The operation service is
/// substituted, so a test reads which resources the command asked it to delete.
/// </summary>
[TestFixture]
public class DeleteResourceCommandTests
{
    private string _projectFolderPath = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IResourceOperationService _operationService = null!;
    private IMessengerService _messengerService = null!;

    private readonly List<ResourceKey> _deletedResources = new();
    private readonly List<ResourceOperationFailedMessage> _sentMessages = new();

    [SetUp]
    public void Setup()
    {
        _deletedResources.Clear();
        _sentMessages.Clear();

        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(DeleteResourceCommandTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _operationService = Substitute.For<IResourceOperationService>();
        _operationService.BeginBatch().Returns(Substitute.For<IBatchScope>());
        _operationService.DeleteAsync(Arg.Do<ResourceKey>(resource => _deletedResources.Add(resource)))
            .Returns(Task.FromResult(Result<bool>.Ok(true)));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.RootHandlers.Returns(TestRootHandlers.Create(_projectFolderPath));
        resourceService.Operations.Returns(_operationService);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);
        resourceService.Sidecars.Returns(new SidecarService(_workspaceWrapper));

        _messengerService = new MessengerService();
        _messengerService.Register<ResourceOperationFailedMessage>(this, (_, message) => _sentMessages.Add(message));
    }

    [TearDown]
    public void TearDown()
    {
        _messengerService.UnregisterAll(this);

        if (Directory.Exists(_projectFolderPath))
        {
            Directory.Delete(_projectFolderPath, recursive: true);
        }
    }

    [Test]
    public async Task DeletingAReferencedFile_DeletesItAndLeavesTheReferenceInPlace()
    {
        WriteProjectFile("notes.md", "Notes.");
        WriteProjectFile("index.json", "{\"target\": \"project:notes.md\"}");

        var command = CreateCommand();
        command.Resources = new List<ResourceKey> { new ResourceKey("notes.md") };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        _deletedResources.Should().Equal(new ResourceKey("notes.md"));
        command.ResultValue.BatchOutcome.Should().Be(DeleteBatchOutcome.DeletedAll);

        var resourceResult = command.ResultValue.ResourceResults.Should().ContainSingle().Subject;
        resourceResult.Outcome.Should().Be(DeleteResourceOutcome.Deleted);
        resourceResult.Undoable.Should().BeTrue();

        File.ReadAllText(Path.Combine(_projectFolderPath, "index.json"))
            .Should().Be("{\"target\": \"project:notes.md\"}");
        _sentMessages.Should().BeEmpty();
    }

    [Test]
    public async Task AMissingResource_IsNotFound_AndTheRestOfTheBatchStillDeletes()
    {
        WriteProjectFile("notes.md", "Notes.");

        var command = CreateCommand();
        command.Resources = new List<ResourceKey>
        {
            new ResourceKey("missing.md"),
            new ResourceKey("notes.md")
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        _deletedResources.Should().Equal(new ResourceKey("notes.md"));
        command.ResultValue.BatchOutcome.Should().Be(DeleteBatchOutcome.DeletedSome);
        command.ResultValue.ResourceResults.Select(entry => entry.Outcome)
            .Should().Equal(DeleteResourceOutcome.NotFound, DeleteResourceOutcome.Deleted);

        var message = _sentMessages.Should().ContainSingle().Subject;
        message.FailedResources.Should().ContainSingle()
            .Which.Resource.Should().Be(new ResourceKey("missing.md"));
    }

    [Test]
    public async Task AFileInUse_IsReportedAsLocked()
    {
        WriteProjectFile("notes.md", "Notes.");
        _operationService.DeleteAsync(new ResourceKey("notes.md"))
            .Returns(Task.FromResult<Result<bool>>(
                Result.Fail("The file is in use.").WithException(new IOException("The file is in use."))));

        var command = CreateCommand();
        command.Resources = new List<ResourceKey> { new ResourceKey("notes.md") };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        command.ResultValue.BatchOutcome.Should().Be(DeleteBatchOutcome.DeletedSome);

        var resourceResult = command.ResultValue.ResourceResults.Should().ContainSingle().Subject;
        resourceResult.Outcome.Should().Be(DeleteResourceOutcome.Locked);
        resourceResult.Undoable.Should().BeFalse();
    }

    private DeleteResourceCommand CreateCommand()
    {
        var operationNotifier = new ResourceOperationNotifier(
            Substitute.For<ILogger<ResourceOperationNotifier>>(),
            _messengerService,
            Substitute.For<IProjectService>(),
            Substitute.For<IReportWriter>(),
            new TestLocalizerService());

        return new DeleteResourceCommand(
            Substitute.For<ILogger<DeleteResourceCommand>>(),
            _workspaceWrapper,
            operationNotifier);
    }

    private void WriteProjectFile(string relativePath, string content)
    {
        var filePath = Path.Combine(_projectFolderPath, relativePath);
        var folderPath = Path.GetDirectoryName(filePath);
        Guard.IsNotNull(folderPath);

        Directory.CreateDirectory(folderPath);
        File.WriteAllText(filePath, content);
    }
}
