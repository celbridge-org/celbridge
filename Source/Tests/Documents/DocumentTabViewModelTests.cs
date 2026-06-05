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
    private IResourceRegistry _resourceRegistry = null!;
    private IResourceOperationService _resourceOperations = null!;
    private IResourceService _resourceService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private readonly List<DocumentTabViewModel> _createdViewModels = new();

    [SetUp]
    public void Setup()
    {
        _messengerService = new MessengerService();
        _commandService = Substitute.For<ICommandService>();
        _logger = Substitute.For<ILogger<DocumentTabViewModel>>();

        _resourceFileSystem = Substitute.For<IResourceFileSystem>();
        var existingFileInfo = new StorageItemInfo(StorageItemKind.File, 0, DateTime.UtcNow, FileSystemAttributes.None);
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>()).Returns(Result<StorageItemInfo>.Ok(existingFileInfo));

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        // Default to "not in registry" so stale OnResourceRegistryUpdatedMessage
        // handlers from prior tests (kept alive by the WeakReferenceMessenger)
        // don't null-deref when a new test broadcasts a registry-updated message.
        _resourceRegistry.GetResource(Arg.Any<ResourceKey>())
            .Returns(Result<IResource>.Fail("not found"));

        _resourceOperations = Substitute.For<IResourceOperationService>();
        _resourceOperations.GetWritableStateAsync(Arg.Any<ResourceKey>()).Returns(WritableState.Writable);

        _resourceService = Substitute.For<IResourceService>();
        _resourceService.Registry.Returns(_resourceRegistry);
        _resourceService.FileSystem.Returns(_resourceFileSystem);
        _resourceService.Operations.Returns(_resourceOperations);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(_resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    [TearDown]
    public void TearDown()
    {
        // The shared WeakReferenceMessenger keeps live registrations across tests
        // until GC. Unregister every view model the test created so its message
        // handlers don't fire against torn-down test state in later tests.
        foreach (var viewModel in _createdViewModels)
        {
            _messengerService.UnregisterAll(viewModel);
        }
        _createdViewModels.Clear();
    }

    private DocumentTabViewModel CreateViewModel(ResourceKey fileResource, IDocumentView? documentView = null)
    {
        var viewModel = new DocumentTabViewModel(_messengerService, _commandService, _logger, _workspaceWrapper)
        {
            FileResource = fileResource,
            DocumentView = documentView!,
        };
        _createdViewModels.Add(viewModel);
        return viewModel;
    }

    [Test]
    public async Task CloseDocument_CompletesAsClosed_WhenSaveFails()
    {
        var documentView = Substitute.For<IDocumentView>();
        documentView.CanClose().Returns(Task.FromResult(true));
        documentView.HasUnsavedChanges.Returns(true);
        documentView.SaveDocument().Returns(Task.FromResult<Result>(Result.Fail("simulated save failure")));

        var viewModel = CreateViewModel(new ResourceKey("locked.md"), documentView);

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

        var viewModel = CreateViewModel(new ResourceKey("writable.md"), documentView);

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

        var viewModel = CreateViewModel(new ResourceKey("vetoed.md"), documentView);

        var result = await viewModel.CloseDocument(forceClose: false);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(CloseDocumentOutcome.Cancelled);
        await documentView.DidNotReceive().SaveDocument();
        await documentView.DidNotReceive().PrepareToClose();
    }

    [Test]
    public async Task CloseDocument_SchedulesResourceUpdate_WhenSaveFailsAndViewStillReportsWritable()
    {
        // A save failure against a view whose WritableState still reads Writable
        // suggests an external attribute flip slipped past the watcher. The close
        // path schedules a resource update so the cache catches up.
        var documentView = Substitute.For<IDocumentView>();
        documentView.CanClose().Returns(Task.FromResult(true));
        documentView.HasUnsavedChanges.Returns(true);
        documentView.SaveDocument().Returns(Task.FromResult<Result>(Result.Fail("simulated save failure")));
        documentView.WritableState.Returns(WritableState.Writable);

        var viewModel = CreateViewModel(new ResourceKey("stale.md"), documentView);

        await viewModel.CloseDocument(forceClose: false);

        _commandService.ReceivedWithAnyArgs(1).Execute<IUpdateResourcesCommand>();
    }

    [Test]
    public async Task CloseDocument_DoesNotScheduleResourceUpdate_WhenCacheAlreadyKnowsNonWritable()
    {
        var documentView = Substitute.For<IDocumentView>();
        documentView.CanClose().Returns(Task.FromResult(true));
        documentView.HasUnsavedChanges.Returns(true);
        documentView.SaveDocument().Returns(Task.FromResult<Result>(Result.Fail("simulated save failure")));
        documentView.WritableState.Returns(WritableState.Locked);

        var viewModel = CreateViewModel(new ResourceKey("locked.md"), documentView);

        await viewModel.CloseDocument(forceClose: false);

        _commandService.DidNotReceiveWithAnyArgs().Execute<IUpdateResourcesCommand>();
    }

    [Test]
    public void ResourceRegistryUpdated_RequeriesAndAppliesNewWritableState_WhenStateHasChanged()
    {
        var documentView = Substitute.For<IDocumentView>();
        documentView.WritableState.Returns(WritableState.Writable);

        var fileResource = new ResourceKey("file.md");
        var resource = Substitute.For<IResource>();
        _resourceRegistry.GetResource(fileResource).Returns(Result<IResource>.Ok(resource));
        _resourceOperations.GetWritableStateAsync(fileResource).Returns(WritableState.ReadOnlyAttribute);

        var viewModel = CreateViewModel(fileResource, documentView);

        _messengerService.Send(new ResourceRegistryUpdatedMessage());

        documentView.Received(1).SetWritableState(WritableState.ReadOnlyAttribute);
    }

    [Test]
    public void ResourceRegistryUpdated_RequeriesAndAppliesNewWritableState_OnUnsetDirection()
    {
        // Mirrors the user-visible "clear read-only externally" path: the cached
        // state transitions from ReadOnlyAttribute back to Writable and the editor
        // must lose its read-only signal.
        var documentView = Substitute.For<IDocumentView>();
        documentView.WritableState.Returns(WritableState.ReadOnlyAttribute);

        var fileResource = new ResourceKey("file.md");
        var resource = Substitute.For<IResource>();
        _resourceRegistry.GetResource(fileResource).Returns(Result<IResource>.Ok(resource));
        _resourceOperations.GetWritableStateAsync(fileResource).Returns(WritableState.Writable);

        var viewModel = CreateViewModel(fileResource, documentView);

        _messengerService.Send(new ResourceRegistryUpdatedMessage());

        documentView.Received(1).SetWritableState(WritableState.Writable);
    }

    [Test]
    public void ResourceRegistryUpdated_SkipsSetWritableState_WhenStateIsUnchanged()
    {
        var documentView = Substitute.For<IDocumentView>();
        documentView.WritableState.Returns(WritableState.Writable);

        var fileResource = new ResourceKey("file.md");
        var resource = Substitute.For<IResource>();
        _resourceRegistry.GetResource(fileResource).Returns(Result<IResource>.Ok(resource));
        _resourceOperations.GetWritableStateAsync(fileResource).Returns(WritableState.Writable);

        var viewModel = CreateViewModel(fileResource, documentView);

        _messengerService.Send(new ResourceRegistryUpdatedMessage());

        documentView.DidNotReceive().SetWritableState(Arg.Any<WritableState>());
    }
}
