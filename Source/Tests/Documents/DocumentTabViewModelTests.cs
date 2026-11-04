using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Tests.Helpers;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Tests for DocumentTabViewModel: the close path, the editor's pip, the menu predicates derived from the
/// tab's file resource, and opening the document in another editor. The save-failure tolerance is load-bearing for locked or otherwise read-only
/// documents, where the save will never succeed and the close path must still complete.
/// </summary>
[TestFixture]
public class DocumentTabViewModelTests
{
    private IMessengerService _messengerService = null!;
    private ICommandService _commandService = null!;
    private ILogger<DocumentTabViewModel> _logger = null!;
    private IResourceFileSystem _resourceFileSystem = null!;
    private FakeResourceMonitor _resourceMonitor = null!;
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

        _resourceMonitor = new FakeResourceMonitor();

        _resourceOperations = Substitute.For<IResourceOperationService>();
        _resourceOperations.GetWritableStateAsync(Arg.Any<ResourceKey>()).Returns(WritableState.Writable);

        _resourceService = Substitute.For<IResourceService>();
        _resourceService.Monitor.Returns(_resourceMonitor);
        _resourceService.FileSystem.Returns(_resourceFileSystem);
        _resourceService.Operations.Returns(_resourceOperations);

        _workspaceService = Substitute.For<IWorkspaceService>();
        _workspaceService.ResourceService.Returns(_resourceService);
        _workspaceService.GetRetryingResources().Returns(Array.Empty<ResourceKey>());

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(_workspaceService);
        _workspaceWrapper.HasWorkspaceService.Returns(true);

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
    public void AChange_AppliesANewWritableState()
    {
        var documentView = Substitute.For<IDocumentView>();
        documentView.WritableState.Returns(WritableState.Writable);

        var fileResource = new ResourceKey("file.md");
        _resourceOperations.GetWritableStateAsync(fileResource).Returns(WritableState.ReadOnlyAttribute);

        CreateViewModel(fileResource, documentView);

        _resourceMonitor.DeliverChange(fileResource, ResourceItemState.File);

        documentView.Received(1).SetWritableState(WritableState.ReadOnlyAttribute);
    }

    [Test]
    public void AChange_AppliesANewWritableState_OnUnsetDirection()
    {
        // Mirrors the user-visible "clear read-only externally" path: the state transitions from ReadOnlyAttribute
        // back to Writable and the editor must lose its read-only signal.
        var documentView = Substitute.For<IDocumentView>();
        documentView.WritableState.Returns(WritableState.ReadOnlyAttribute);

        var fileResource = new ResourceKey("file.md");
        _resourceOperations.GetWritableStateAsync(fileResource).Returns(WritableState.Writable);

        CreateViewModel(fileResource, documentView);

        _resourceMonitor.DeliverChange(fileResource, ResourceItemState.File);

        documentView.Received(1).SetWritableState(WritableState.Writable);
    }

    [Test]
    public void AChange_SkipsSetWritableState_WhenStateIsUnchanged()
    {
        var documentView = Substitute.For<IDocumentView>();
        documentView.WritableState.Returns(WritableState.Writable);

        var fileResource = new ResourceKey("file.md");
        _resourceOperations.GetWritableStateAsync(fileResource).Returns(WritableState.Writable);

        CreateViewModel(fileResource, documentView);

        _resourceMonitor.DeliverChange(fileResource, ResourceItemState.File);

        documentView.DidNotReceive().SetWritableState(Arg.Any<WritableState>());
    }

    [Test]
    public void AResync_AppliesANewWritableState()
    {
        // The lost events may have included an attribute change.
        var documentView = Substitute.For<IDocumentView>();
        documentView.WritableState.Returns(WritableState.Writable);

        var fileResource = new ResourceKey("file.md");
        _resourceOperations.GetWritableStateAsync(fileResource).Returns(WritableState.ReadOnlyAttribute);

        CreateViewModel(fileResource, documentView);

        _resourceMonitor.DeliverResync(ResourceKey.DefaultRoot);

        documentView.Received(1).SetWritableState(WritableState.ReadOnlyAttribute);
    }

    [Test]
    public void AChange_ClosesTheTab_WhenTheFileIsAbsentFromDisk()
    {
        var fileResource = new ResourceKey("deleted.md");
        CreateViewModel(fileResource, Substitute.For<IDocumentView>());
        StubFileIsGone();

        _resourceMonitor.DeliverChange(fileResource, ResourceItemState.Absent);

        _commandService.ReceivedWithAnyArgs(1).Execute<ICloseDocumentCommand>();
    }

    [Test]
    public void AChange_LeavesTheTabOpen_OnceTheWorkspaceIsGone()
    {
        var fileResource = new ResourceKey("deleted.md");
        CreateViewModel(fileResource, Substitute.For<IDocumentView>());
        StubFileIsGone();
        _workspaceWrapper.HasWorkspaceService.Returns(false);
        _workspaceWrapper.WorkspaceService.Returns(_ => throw new InvalidOperationException("No workspace is loaded"));

        _resourceMonitor.DeliverChange(fileResource, ResourceItemState.Absent);

        _commandService.DidNotReceiveWithAnyArgs().Execute<ICloseDocumentCommand>();
    }

    [Test]
    public void AnAbsentChange_LeavesTheTabOpen_WhenTheFileIsBackOnDisk()
    {
        // A save can write a temporary file, delete the original and rename the temporary file. The file is then back
        // in place by the time the tab checks the disk.
        var fileResource = new ResourceKey("saved.md");
        CreateViewModel(fileResource, Substitute.For<IDocumentView>());

        _resourceMonitor.DeliverChange(fileResource, ResourceItemState.Absent);

        _commandService.DidNotReceiveWithAnyArgs().Execute<ICloseDocumentCommand>();
    }

    [Test]
    public void AKeyChange_IsAppliedAtOnce()
    {
        var oldResource = new ResourceKey("old.md");
        var newResource = new ResourceKey("new.md");
        CreateViewModel(oldResource, Substitute.For<IDocumentView>());

        var documentChanges = new List<DocumentResourceChangedMessage>();
        var probe = new object();
        _messengerService.Register<DocumentResourceChangedMessage>(
            probe,
            (object _, DocumentResourceChangedMessage message) => documentChanges.Add(message));

        _messengerService.Send(new ResourceKeyChangedMessage(oldResource, newResource));

        _messengerService.UnregisterAll(probe);

        documentChanges.Should().Equal(new DocumentResourceChangedMessage(oldResource, newResource));
    }

    [Test]
    public void AKeyChange_FollowsTheNewKeyBeforeTheTabTakesIt()
    {
        // The watcher reports the old key absent moments after the move, while the document may still be loading
        // under its new key.
        var oldResource = new ResourceKey("old.md");
        var newResource = new ResourceKey("new.md");
        CreateViewModel(oldResource, Substitute.For<IDocumentView>());

        _messengerService.Send(new ResourceKeyChangedMessage(oldResource, newResource));

        _resourceMonitor.KeySubscriptions.Single().Resources.Should().Equal(newResource);
    }

    [Test]
    public void AKeyChangeOfAFolderAboveTheFile_IsMappedByPrefix()
    {
        // The gateway sends one message for a moved folder, which names the folder alone.
        var oldResource = new ResourceKey("docs/guides/intro.md");
        var newResource = new ResourceKey("archive/guides/intro.md");
        CreateViewModel(oldResource, Substitute.For<IDocumentView>());

        var documentChanges = new List<DocumentResourceChangedMessage>();
        var probe = new object();
        _messengerService.Register<DocumentResourceChangedMessage>(
            probe,
            (object _, DocumentResourceChangedMessage message) => documentChanges.Add(message));

        _messengerService.Send(new ResourceKeyChangedMessage(new ResourceKey("docs"), new ResourceKey("archive")));

        _messengerService.UnregisterAll(probe);

        documentChanges.Should().Equal(new DocumentResourceChangedMessage(oldResource, newResource));
        _resourceMonitor.KeySubscriptions.Single().Resources.Should().Equal(newResource);
    }

    [Test]
    public void AKeyChangeOfASiblingWithTheSamePrefix_LeavesTheTabAlone()
    {
        var resource = new ResourceKey("docs-old/intro.md");
        CreateViewModel(resource, Substitute.For<IDocumentView>());

        _messengerService.Send(new ResourceKeyChangedMessage(new ResourceKey("docs"), new ResourceKey("archive")));

        _resourceMonitor.KeySubscriptions.Single().Resources.Should().Equal(resource);
    }

    [Test]
    public async Task CloseDocument_DisposesTheSubscription()
    {
        var documentView = Substitute.For<IDocumentView>();
        documentView.CanClose().Returns(Task.FromResult(true));
        var viewModel = CreateViewModel(new ResourceKey("file.md"), documentView);

        await viewModel.CloseDocument(forceClose: false);

        _resourceMonitor.KeySubscriptions.Should().BeEmpty();
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
    public void APipChange_ForTheTabsResource_SetsTheTabsPip()
    {
        var fileResource = new ResourceKey("notes.md");
        var viewModel = CreateViewModel(fileResource);
        var pip = new WorkspaceItemPip(PipKind.Alert, "Syncing", IsPulsing: true);

        _messengerService.Send(new WorkspaceItemPipChangedMessage(fileResource, pip));

        viewModel.Pip.Should().Be(pip);
        viewModel.PipKind.Should().Be(PipKind.Alert);
        viewModel.PipLabel.Should().Be("Syncing");
        viewModel.IsPipPulsing.Should().BeTrue();

        _messengerService.Send(new WorkspaceItemPipChangedMessage(fileResource, null));

        viewModel.PipKind.Should().BeNull();
        viewModel.PipLabel.Should().BeEmpty();
        viewModel.IsPipPulsing.Should().BeFalse();
    }

    [Test]
    public void APipChange_ForAnotherResource_IsIgnored()
    {
        var viewModel = CreateViewModel(new ResourceKey("notes.md"));
        var pip = new WorkspaceItemPip(PipKind.Alert, "Syncing", IsPulsing: false);

        _messengerService.Send(new WorkspaceItemPipChangedMessage(new ResourceKey("other.md"), pip));

        viewModel.Pip.Should().BeNull();
    }

    [Test]
    public void SettingTheView_ReadsThePipTheViewAlreadyHas()
    {
        // A docked utility's new tab missed the pip change, which was sent before the tab existed.
        var pip = new WorkspaceItemPip(PipKind.Notice, "Low disk space", IsPulsing: false);
        var documentView = Substitute.For<IDocumentView>();
        documentView.Pip.Returns(pip);

        var viewModel = CreateViewModel(new ResourceKey("utils:acme.tools._tools"), documentView);

        viewModel.Pip.Should().Be(pip);
    }

    [Test]
    public void TabTooltip_AddsThePipLabelBelowTheDescription()
    {
        var fileResource = new ResourceKey("notes.md");
        var viewModel = CreateViewModel(fileResource);
        viewModel.FilePath = "C:/project/notes.md";

        _messengerService.Send(new WorkspaceItemPipChangedMessage(fileResource, new WorkspaceItemPip(PipKind.Neutral, "Syncing", IsPulsing: false)));

        viewModel.TabTooltip.Should().Be("StatusPip_Tooltip_WithLabel(C:/project/notes.md, Syncing)");
    }

    [Test]
    public void TabTooltip_SaysTheFileCouldNotBeSaved_BelowThePipLabel()
    {
        var fileResource = new ResourceKey("notes.md");
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceService.GetRetryingResources().Returns(new[] { fileResource });

        var viewModel = CreateViewModel(fileResource);
        viewModel.FilePath = "C:/project/notes.md";

        _messengerService.Send(new WorkspaceItemPipChangedMessage(fileResource, new WorkspaceItemPip(PipKind.Neutral, "Syncing", IsPulsing: false)));

        viewModel.TabTooltip.Should().Be(
            "DocumentTab_Tooltip_SaveFailed(StatusPip_Tooltip_WithLabel(C:/project/notes.md, Syncing))");
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

    [Test]
    public async Task OpenWithEditorAsync_RecordsTheChoiceAndOpensTheDocumentInThatEditor()
    {
        var fileResource = new ResourceKey("docs/notes.md");
        var codeEditor = new EditorId("acme.code");
        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.SetPreferredEditorAsync(Arg.Any<ResourceKey>(), Arg.Any<EditorId>())
            .Returns(Task.FromResult(Result.Ok()));
        _workspaceService.DocumentsService.Returns(documentsService);
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        Action<IOpenDocumentCommand>? configure = null;
        _commandService.Execute(
            Arg.Do<Action<IOpenDocumentCommand>>(action => configure = action),
            Arg.Any<string>(),
            Arg.Any<int>());
        var viewModel = CreateViewModel(fileResource);

        await viewModel.OpenWithEditorAsync(codeEditor);

        await documentsService.Received(1).SetPreferredEditorAsync(fileResource, codeEditor);
        configure.Should().NotBeNull();
        var openCommand = Substitute.For<IOpenDocumentCommand>();
        configure!(openCommand);
        openCommand.FileResource.Should().Be(fileResource);
        openCommand.EditorId.Should().Be(codeEditor);
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
