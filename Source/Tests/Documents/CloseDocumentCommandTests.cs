using Celbridge.Documents.Commands;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Verifies CloseDocumentCommand's routing: an ordinary document is closed through IDocumentsService.CloseDocument,
/// while a docked utility is docked back into the Utility Panel (via IUtilityService) instead of destroyed.
/// Centralizing the decision here means every close path (the tab close button, close shortcuts, the tab context
/// menu, bulk closes, and programmatic and MCP callers) returns a utility to the panel rather than tearing it down.
/// </summary>
[TestFixture]
public class CloseDocumentCommandTests
{
    private static readonly EditorInstanceId NotepadUtilityId = EditorInstanceId.Create("acme", "notepad");

    private IDocumentsService _documentsService = null!;
    private IUtilityService _utilityService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _documentsService = Substitute.For<IDocumentsService>();
        _documentsService.CloseDocument(Arg.Any<ResourceKey>(), Arg.Any<bool>()).Returns(Result.Ok());

        _utilityService = Substitute.For<IUtilityService>();
        _utilityService.DockUtilityAsync(Arg.Any<EditorInstanceId>(), Arg.Any<DockLocation>()).Returns(Result.Ok());

        // By default a resource is not a docked utility, so the command takes the ordinary close path.
        _utilityService.GetDockedUtilityId(Arg.Any<ResourceKey>()).Returns((EditorInstanceId?)null);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(_documentsService);
        workspaceService.UtilityService.Returns(_utilityService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    private CloseDocumentCommand CreateCommand()
    {
        return new CloseDocumentCommand(_workspaceWrapper);
    }

    [Test]
    public async Task ExecuteAsync_OrdinaryDocument_ClosesThroughDocumentsService()
    {
        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");
        command.ForceClose = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _documentsService.Received(1).CloseDocument(new ResourceKey("notes/readme.md"), true);
        await _utilityService.DidNotReceive().DockUtilityAsync(Arg.Any<EditorInstanceId>(), Arg.Any<DockLocation>());
    }

    [Test]
    public async Task ExecuteAsync_DockedUtility_DocksBackToPanelInsteadOfClosing()
    {
        var utilityResource = new ResourceKey("utils:settings._notepad");
        _utilityService.GetDockedUtilityId(utilityResource).Returns(NotepadUtilityId);

        var command = CreateCommand();
        command.FileResource = utilityResource;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        // The utility is docked back into the panel, never destroyed through the close path.
        await _utilityService.Received(1).DockUtilityAsync(NotepadUtilityId, DockLocation.UtilityPanel);
        await _documentsService.DidNotReceive().CloseDocument(Arg.Any<ResourceKey>(), Arg.Any<bool>());
    }

    [Test]
    public async Task ExecuteAsync_DockedUtilityDockFails_PropagatesFailure()
    {
        var utilityResource = new ResourceKey("utils:settings._notepad");
        _utilityService.GetDockedUtilityId(utilityResource).Returns(NotepadUtilityId);
        _utilityService.DockUtilityAsync(NotepadUtilityId, DockLocation.UtilityPanel).Returns(Result.Fail("Dock failed"));

        var command = CreateCommand();
        command.FileResource = utilityResource;

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        await _documentsService.DidNotReceive().CloseDocument(Arg.Any<ResourceKey>(), Arg.Any<bool>());
    }
}
