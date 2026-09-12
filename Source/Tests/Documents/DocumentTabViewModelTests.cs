using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Tests for DocumentTabViewModel close-path behaviour and the menu predicates derived from its file
/// resource. The save-failure tolerance is load-bearing for locked or otherwise read-only documents,
/// where the save will never succeed and the close path must still complete.
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
    private IStringLocalizer _stringLocalizer = null!;
    private IWorkspaceService _workspaceService = null!;
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

        _workspaceService = Substitute.For<IWorkspaceService>();
        _workspaceService.ResourceService.Returns(_resourceService);
        _workspaceService.GetRetryingResources().Returns(Array.Empty<ResourceKey>());

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(_workspaceService);

        // The composed value carries the arguments, so a test can tell which values reached the template.
        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(call =>
        {
            var name = call.Arg<string>();
            var arguments = call.Arg<object[]>();
            return new LocalizedString(name, $"{name}({string.Join(", ", arguments)})");
        });
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
        var viewModel = new DocumentTabViewModel(
            _messengerService,
            _commandService,
            _logger,
            _workspaceWrapper,
            _stringLocalizer)
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
        documentView.SaveAsync().Returns(Task.FromResult<Result>(Result.Fail("simulated save failure")));

        var viewModel = CreateViewModel(new ResourceKey("readonly.md"), documentView);

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
        documentView.SaveAsync().Returns(Task.FromResult<Result>(Result.Ok()));

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
        await documentView.DidNotReceive().SaveAsync();
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
        documentView.SaveAsync().Returns(Task.FromResult<Result>(Result.Fail("simulated save failure")));
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
        documentView.SaveAsync().Returns(Task.FromResult<Result>(Result.Fail("simulated save failure")));
        documentView.WritableState.Returns(WritableState.ReadOnlyAttribute);

        var viewModel = CreateViewModel(new ResourceKey("readonly.md"), documentView);

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

    [Test]
    public async Task CloseDocument_AnnouncesDiscardedEdits_WhenSaveFails()
    {
        var fileResource = new ResourceKey("locked.md");
        var viewModel = CreateViewModel(fileResource, CreateUnwritableDocumentView());

        var discardedResources = RecordDiscardedResources(out var probe);

        await viewModel.CloseDocument(forceClose: false);

        _messengerService.UnregisterAll(probe);

        discardedResources.Should().Equal(new[] { fileResource },
            "the edits went with the view, so this is the last chance to say so");
    }

    [Test]
    public async Task CloseDocument_AnnouncesDiscardedEdits_WhenTheFileIsGone()
    {
        // An external folder rename force-closes the documents inside it, and their edits have nowhere
        // left to go. Silently dropping them is the one outcome the user cannot recover from.
        var fileResource = new ResourceKey("renamed/notes.md");
        var documentView = Substitute.For<IDocumentView>();
        documentView.CanClose().Returns(Task.FromResult(true));
        documentView.HasUnsavedChanges.Returns(true);
        StubFileIsGone();

        var viewModel = CreateViewModel(fileResource, documentView);

        var discardedResources = RecordDiscardedResources(out var probe);

        await viewModel.CloseDocument(forceClose: true);

        _messengerService.UnregisterAll(probe);

        discardedResources.Should().Equal(new[] { fileResource });
        await documentView.DidNotReceive().SaveAsync();
    }

    [Test]
    public async Task CloseDocument_AnnouncesNothing_WhenTheFileIsGoneWithNoPendingEdits()
    {
        var documentView = Substitute.For<IDocumentView>();
        documentView.CanClose().Returns(Task.FromResult(true));
        documentView.HasUnsavedChanges.Returns(false);
        StubFileIsGone();

        var viewModel = CreateViewModel(new ResourceKey("deleted.md"), documentView);

        var discardedResources = RecordDiscardedResources(out var probe);

        await viewModel.CloseDocument(forceClose: true);

        _messengerService.UnregisterAll(probe);

        discardedResources.Should().BeEmpty("a deliberate delete of a saved document discards nothing");
    }

    [Test]
    public async Task CloseDocument_DoesNotAnnounceDiscardedEdits_ForADockedUtility()
    {
        var viewModel = CreateViewModel(new ResourceKey("locked.md"), CreateUnwritableDocumentView());
        viewModel.IsDockedUtility = true;

        var discardedResources = RecordDiscardedResources(out var probe);

        await viewModel.CloseDocument(forceClose: false);

        _messengerService.UnregisterAll(probe);

        discardedResources.Should().BeEmpty("a docked utility keeps its view and its content when the tab closes");
    }

    [Test]
    public void NewTab_ShowsTheWarning_WhenTheResourceIsAlreadyRetrying()
    {
        var fileResource = new ResourceKey("locked.md");
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceService.GetRetryingResources().Returns(new[] { fileResource });

        var viewModel = CreateViewModel(fileResource);

        viewModel.IsSaveRetrying.Should().BeTrue("the failing set is only sent when it changes");
    }

    [Test]
    public void TabTooltip_SaysTheFileCouldNotBeSaved_WhenTheResourceIsFailing()
    {
        var fileResource = new ResourceKey("locked.md");
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceService.GetRetryingResources().Returns(new[] { fileResource });

        var viewModel = CreateViewModel(fileResource);
        viewModel.FilePath = "C:/project/locked.md";

        viewModel.TabTooltip.Should()
            .StartWith("DocumentTab_Tooltip_SaveFailed(")
            .And.Contain("C:/project/locked.md");
    }

    [Test]
    public void CanRevealInExplorer_IsTrue_ForProjectResource()
    {
        var viewModel = CreateViewModel(new ResourceKey("docs/notes.md"));

        viewModel.CanRevealInExplorer.Should().BeTrue();
    }

    [Test]
    public void CanRevealInExplorer_IsFalse_ForNonProjectRoot()
    {
        // The Explorer shows the project tree, so a document opened from any other root has nothing to
        // reveal and the menu option is hidden.
        var viewModel = CreateViewModel(new ResourceKey("temp:community.webview"));

        viewModel.CanRevealInExplorer.Should().BeFalse();
    }

    private static IDocumentView CreateUnwritableDocumentView()
    {
        var documentView = Substitute.For<IDocumentView>();
        documentView.CanClose().Returns(Task.FromResult(true));
        documentView.HasUnsavedChanges.Returns(true);
        documentView.SaveAsync().Returns(Task.FromResult<Result>(Result.Fail("simulated save failure")));
        documentView.WritableState.Returns(WritableState.ReadOnlyAttribute);

        return documentView;
    }

    private void StubFileIsGone()
    {
        var missingFileInfo = new StorageItemInfo(StorageItemKind.NotFound, 0, default, FileSystemAttributes.None);
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>()).Returns(Result<StorageItemInfo>.Ok(missingFileInfo));
    }

    private List<ResourceKey> RecordDiscardedResources(out object probe)
    {
        var discardedResources = new List<ResourceKey>();

        probe = new object();
        _messengerService.Register<WorkspaceItemSaveDiscardedMessage>(
            probe,
            (object _, WorkspaceItemSaveDiscardedMessage message) => discardedResources.Add(message.Resource));

        return discardedResources;
    }
}
