using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents.Commands;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Verifies that OpenDocumentCommand forwards its public options to
/// IDocumentsService.OpenDocument via the OpenDocumentOptions record.
/// </summary>
[TestFixture]
public class OpenDocumentCommandTests
{
    private IDocumentsService _documentsService = null!;
    private IDocumentsPanel _documentsPanel = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IStringLocalizer _stringLocalizer = null!;
    private IDialogService _dialogService = null!;
    private ICommandService _commandService = null!;
    private IMessengerService _messengerService = null!;
    private ILayoutService _layoutService = null!;
    private IWindowModeService _windowModeService = null!;

    [SetUp]
    public void Setup()
    {
        _documentsService = Substitute.For<IDocumentsService>();
        _documentsService.GetDocumentViewType(Arg.Any<ResourceKey>()).Returns(DocumentViewType.TextDocument);
        _documentsService
            .OpenDocument(Arg.Any<ResourceKey>(), Arg.Any<OpenDocumentOptions?>())
            .Returns(Result<OpenDocumentOutcome>.Ok(OpenDocumentOutcome.Opened));

        _documentsPanel = Substitute.For<IDocumentsPanel>();

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(_documentsService);
        workspaceService.DocumentsPanel.Returns(_documentsPanel);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _dialogService = Substitute.For<IDialogService>();
        _commandService = Substitute.For<ICommandService>();

        _messengerService = Substitute.For<IMessengerService>();
        _layoutService = Substitute.For<ILayoutService>();

        _windowModeService = Substitute.For<IWindowModeService>();
        _windowModeService.LayoutMode.Returns(LayoutMode.Default);
    }

    private OpenDocumentCommand CreateCommand()
    {
        return new OpenDocumentCommand(
            _stringLocalizer,
            _dialogService,
            _commandService,
            _workspaceWrapper,
            _messengerService,
            _layoutService,
            _windowModeService);
    }

    [Test]
    public async Task ExecuteAsync_WithNoTargetSection_PassesNullAddress()
    {
        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _documentsService.Received(1).OpenDocument(
            new ResourceKey("notes/readme.md"),
            Arg.Is<OpenDocumentOptions>(options => options.Address == null));
    }

    [Test]
    public async Task ExecuteAsync_WithTargetSection_BuildsDocumentAddress()
    {
        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");
        command.TargetSection = DocumentSection.BottomLeft;
        command.TargetTabIndex = 5;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _documentsService.Received(1).OpenDocument(
            new ResourceKey("notes/readme.md"),
            Arg.Is<OpenDocumentOptions>(options =>
                options.Address != null &&
                options.Address.Section == DocumentSection.BottomLeft &&
                options.Address.TabOrder == 5));
    }

    [Test]
    public async Task ExecuteAsync_WithTargetSectionButNoTab_AppendsToTheTabRow()
    {
        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");
        command.TargetSection = DocumentSection.MainRight;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _documentsService.Received(1).OpenDocument(
            new ResourceKey("notes/readme.md"),
            Arg.Is<OpenDocumentOptions>(options =>
                options.Address != null &&
                options.Address.Section == DocumentSection.MainRight &&
                options.Address.TabOrder == DocumentAddress.AppendTabOrder));
    }

    [Test]
    public async Task ExecuteAsync_ForwardsForceReloadLocationActivateEditorIdAndState()
    {
        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");
        command.ForceReload = true;
        command.Location = "line:42";
        command.Activate = false;
        command.EditorId = new EditorId("celbridge.markdown-editor");
        command.EditorStateJson = "{\"scroll\":0.5}";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _documentsService.Received(1).OpenDocument(
            new ResourceKey("notes/readme.md"),
            Arg.Is<OpenDocumentOptions>(options =>
                options.ForceReload == true &&
                options.Location == "line:42" &&
                options.Activate == false &&
                options.EditorId == new EditorId("celbridge.markdown-editor") &&
                options.EditorStateJson == "{\"scroll\":0.5}"));
    }

    [Test]
    public async Task ExecuteAsync_PropagatesOpenDocumentOutcomeToResultValue()
    {
        _documentsService
            .OpenDocument(Arg.Any<ResourceKey>(), Arg.Any<OpenDocumentOptions?>())
            .Returns(Result<OpenDocumentOutcome>.Ok(OpenDocumentOutcome.Cancelled));

        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.Should().Be(OpenDocumentOutcome.Cancelled);
    }

    [Test]
    public async Task ExecuteAsync_WithTargetSectionAndNoActivation_ShowsTheArea()
    {
        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");
        command.TargetSection = DocumentSection.SideTop;
        command.Activate = false;

        await command.ExecuteAsync();

        _layoutService.Received(1).SetAreaVisibility(WorkspaceArea.Side, true);
    }

    [TestCase(LayoutMode.Focus)]
    [TestCase(LayoutMode.Presentation)]
    public async Task ExecuteAsync_BackgroundOpenInALayoutMode_LeavesTheAreasAlone(LayoutMode layoutMode)
    {
        _windowModeService.LayoutMode.Returns(layoutMode);

        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");
        command.TargetSection = DocumentSection.SideTop;
        command.Activate = false;

        await command.ExecuteAsync();

        _layoutService.DidNotReceiveWithAnyArgs().SetAreaVisibility(default, default);
    }

    [Test]
    public async Task ExecuteAsync_ActivatingOpen_LeavesTheAreaToTheOpenItself()
    {
        // Bringing the document forward shows its area, so the command has nothing to add.
        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");
        command.TargetSection = DocumentSection.BottomLeft;
        command.Activate = true;

        await command.ExecuteAsync();

        _layoutService.DidNotReceiveWithAnyArgs().SetAreaVisibility(default, default);
    }

    [Test]
    public async Task ExecuteAsync_WithNoTargetSection_LeavesTheAreasAlone()
    {
        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");
        command.Activate = false;

        await command.ExecuteAsync();

        _layoutService.DidNotReceiveWithAnyArgs().SetAreaVisibility(default, default);
    }

    [Test]
    public async Task ExecuteAsync_CancelledOpen_LeavesTheAreasAlone()
    {
        _documentsService
            .OpenDocument(Arg.Any<ResourceKey>(), Arg.Any<OpenDocumentOptions?>())
            .Returns(Result<OpenDocumentOutcome>.Ok(OpenDocumentOutcome.Cancelled));

        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");
        command.TargetSection = DocumentSection.BottomLeft;
        command.Activate = false;

        await command.ExecuteAsync();

        _layoutService.DidNotReceiveWithAnyArgs().SetAreaVisibility(default, default);
    }

    [Test]
    public async Task ExecuteAsync_BackgroundOpenIntoASectionShowingNothing_SelectsTheDocument()
    {
        var resource = new ResourceKey("pages/index.html");
        OpenInSection(resource, DocumentSection.MainLeft);
        _documentsPanel.GetSelectedDocument(DocumentSection.MainLeft).Returns(ResourceKey.Empty);

        var command = CreateCommand();
        command.FileResource = resource;
        command.Activate = false;

        await command.ExecuteAsync();

        _documentsPanel.Received(1).SetSelectedDocument(DocumentSection.MainLeft, resource);
    }

    [Test]
    public async Task ExecuteAsync_BackgroundOpenIntoASectionShowingADocument_LeavesTheSelection()
    {
        var resource = new ResourceKey("pages/index.html");
        OpenInSection(resource, DocumentSection.MainLeft);
        _documentsPanel.GetSelectedDocument(DocumentSection.MainLeft).Returns(new ResourceKey("notes/readme.md"));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Activate = false;

        await command.ExecuteAsync();

        _documentsPanel.DidNotReceiveWithAnyArgs().SetSelectedDocument(default, default);
    }

    [Test]
    public async Task ExecuteAsync_BackgroundOpenThatLandsInAnotherSection_SelectsTheDocumentThere()
    {
        // Naming the secondary section of an empty area folds the split back into the primary section.
        var resource = new ResourceKey("pages/index.html");
        OpenInSection(resource, DocumentSection.MainLeft);
        _documentsPanel.GetSelectedDocument(DocumentSection.MainLeft).Returns(ResourceKey.Empty);

        var command = CreateCommand();
        command.FileResource = resource;
        command.TargetSection = DocumentSection.MainRight;
        command.Activate = false;

        await command.ExecuteAsync();

        _documentsPanel.Received(1).SetSelectedDocument(DocumentSection.MainLeft, resource);
    }

    [Test]
    public async Task ExecuteAsync_ActivatingOpen_LeavesTheSelectionToTheOpenItself()
    {
        var resource = new ResourceKey("pages/index.html");
        OpenInSection(resource, DocumentSection.MainLeft);
        _documentsPanel.GetSelectedDocument(DocumentSection.MainLeft).Returns(ResourceKey.Empty);

        var command = CreateCommand();
        command.FileResource = resource;
        command.Activate = true;

        await command.ExecuteAsync();

        _documentsPanel.DidNotReceiveWithAnyArgs().SetSelectedDocument(default, default);
    }

    private void OpenInSection(ResourceKey resource, DocumentSection section)
    {
        var address = new DocumentAddress(WindowIndex: 0, Section: section, TabOrder: 0);
        var openDocument = new OpenDocumentInfo(resource, address, EditorId.Empty);

        _documentsService.FindOpenDocument(resource).Returns(openDocument);
    }
}
