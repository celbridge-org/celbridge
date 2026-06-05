using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Tests for DocumentTabViewModel close-path behaviour. The save-failure tolerance is
/// load-bearing for locked or otherwise read-only documents, where the save will never
/// succeed and the close path must still complete.
/// </summary>
[TestFixture]
public class DocumentTabViewModelTests
{
    private IMessengerService _messengerService = null!;
    private ICommandService _commandService = null!;
    private ILogger<DocumentTabViewModel> _logger = null!;
    private IResourceFileSystem _resourceFileSystem = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = new MessengerService();
        _commandService = Substitute.For<ICommandService>();
        _logger = Substitute.For<ILogger<DocumentTabViewModel>>();

        _resourceFileSystem = Substitute.For<IResourceFileSystem>();
        var existingFileInfo = new StorageItemInfo(StorageItemKind.File, 0, DateTime.UtcNow, FileSystemAttributes.None);
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>()).Returns(Result<StorageItemInfo>.Ok(existingFileInfo));

        var resourceRegistry = Substitute.For<IResourceRegistry>();

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(resourceRegistry);
        resourceService.FileSystem.Returns(_resourceFileSystem);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    [Test]
    public async Task CloseDocument_CompletesAsClosed_WhenSaveFails()
    {
        var documentView = Substitute.For<IDocumentView>();
        documentView.CanClose().Returns(Task.FromResult(true));
        documentView.HasUnsavedChanges.Returns(true);
        documentView.SaveDocument().Returns(Task.FromResult<Result>(Result.Fail("simulated save failure")));

        var viewModel = new DocumentTabViewModel(_messengerService, _commandService, _logger, _workspaceWrapper)
        {
            FileResource = new ResourceKey("locked.md"),
            DocumentView = documentView,
        };

        var result = await viewModel.CloseDocument(forceClose: false);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(CloseDocumentOutcome.Closed);
        await documentView.Received(1).PrepareToClose();
    }

    [Test]
    public async Task CloseDocument_CompletesAsClosed_WhenSaveSucceeds()
    {
        var documentView = Substitute.For<IDocumentView>();
        documentView.CanClose().Returns(Task.FromResult(true));
        documentView.HasUnsavedChanges.Returns(true);
        documentView.SaveDocument().Returns(Task.FromResult<Result>(Result.Ok()));

        var viewModel = new DocumentTabViewModel(_messengerService, _commandService, _logger, _workspaceWrapper)
        {
            FileResource = new ResourceKey("writable.md"),
            DocumentView = documentView,
        };

        var result = await viewModel.CloseDocument(forceClose: false);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(CloseDocumentOutcome.Closed);
        await documentView.Received(1).PrepareToClose();
    }

    [Test]
    public async Task CloseDocument_ReturnsCancelled_WhenViewVetoesClose()
    {
        var documentView = Substitute.For<IDocumentView>();
        documentView.CanClose().Returns(Task.FromResult(false));
        documentView.HasUnsavedChanges.Returns(true);

        var viewModel = new DocumentTabViewModel(_messengerService, _commandService, _logger, _workspaceWrapper)
        {
            FileResource = new ResourceKey("vetoed.md"),
            DocumentView = documentView,
        };

        var result = await viewModel.CloseDocument(forceClose: false);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(CloseDocumentOutcome.Cancelled);
        await documentView.DidNotReceive().SaveDocument();
        await documentView.DidNotReceive().PrepareToClose();
    }
}
