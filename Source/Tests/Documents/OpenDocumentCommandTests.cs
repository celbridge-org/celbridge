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

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(_documentsService);

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
    public async Task ExecuteAsync_ActivatingOpenInALayoutMode_ShowsTheArea()
    {
        _windowModeService.LayoutMode.Returns(LayoutMode.Focus);

        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");
        command.TargetSection = DocumentSection.BottomLeft;
        command.Activate = true;

        await command.ExecuteAsync();

        _layoutService.Received(1).SetAreaVisibility(WorkspaceArea.Bottom, true);
    }

    [Test]
    public async Task ExecuteAsync_WithNoTargetSection_LeavesTheAreasAlone()
    {
        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");

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

        await command.ExecuteAsync();

        _layoutService.DidNotReceiveWithAnyArgs().SetAreaVisibility(default, default);
    }
}
