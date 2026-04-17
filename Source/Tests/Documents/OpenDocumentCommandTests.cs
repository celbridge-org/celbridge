using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents.Commands;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Verifies that OpenDocumentCommand forwards its public options (TargetSectionIndex,
/// TargetTabIndex, ForceReload, Location, Activate, EditorId, EditorStateJson) to
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
    private ILayoutService _layoutService = null!;

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

        _layoutService = Substitute.For<ILayoutService>();
        _layoutService.IsConsoleMaximized.Returns(false);
    }

    private OpenDocumentCommand CreateCommand()
    {
        return new OpenDocumentCommand(
            _stringLocalizer,
            _dialogService,
            _commandService,
            _workspaceWrapper,
            _layoutService);
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
    public async Task ExecuteAsync_WithTargetSectionIndex_BuildsDocumentAddress()
    {
        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");
        command.TargetSectionIndex = 2;
        command.TargetTabIndex = 5;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _documentsService.Received(1).OpenDocument(
            new ResourceKey("notes/readme.md"),
            Arg.Is<OpenDocumentOptions>(options =>
                options.Address != null &&
                options.Address.SectionIndex == 2 &&
                options.Address.TabOrder == 5));
    }

    [Test]
    public async Task ExecuteAsync_WithTargetSectionButNoTab_DefaultsTabOrderToZero()
    {
        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");
        command.TargetSectionIndex = 1;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _documentsService.Received(1).OpenDocument(
            new ResourceKey("notes/readme.md"),
            Arg.Is<OpenDocumentOptions>(options =>
                options.Address != null &&
                options.Address.SectionIndex == 1 &&
                options.Address.TabOrder == 0));
    }

    [Test]
    public async Task ExecuteAsync_ForwardsForceReloadLocationActivateEditorIdAndState()
    {
        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");
        command.ForceReload = true;
        command.Location = "line:42";
        command.Activate = false;
        command.EditorId = new DocumentEditorId("celbridge.markdown-editor");
        command.EditorStateJson = "{\"scroll\":0.5}";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _documentsService.Received(1).OpenDocument(
            new ResourceKey("notes/readme.md"),
            Arg.Is<OpenDocumentOptions>(options =>
                options.ForceReload == true &&
                options.Location == "line:42" &&
                options.Activate == false &&
                options.EditorId == new DocumentEditorId("celbridge.markdown-editor") &&
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
}
