using Celbridge.Documents.Commands;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Verifies CloseDocumentCommand's routing: an ordinary document is closed through IDocumentsService.CloseDocument,
/// while a docked utility is docked back into the Utility Panel (via IUtilityService) instead of destroyed.
/// </summary>
[TestFixture]
public class CloseDocumentCommandTests
{
    private static readonly EditorId NotepadUtilityId = EditorId.Create("acme", "notepad");

    private IDocumentsService _documentsService = null!;
    private IUtilityService _utilityService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _documentsService = Substitute.For<IDocumentsService>();
        _documentsService.CloseDocument(Arg.Any<ResourceKey>(), Arg.Any<CloseDocumentOptions>()).Returns(Result.Ok());

        _utilityService = Substitute.For<IUtilityService>();
        _utilityService.DockUtilityAsync(Arg.Any<EditorId>(), Arg.Any<DockLocation>()).Returns(Result.Ok());

        // By default a resource is not a docked utility, so the command takes the ordinary close path.
        _utilityService.GetDockedUtilityId(Arg.Any<ResourceKey>()).Returns((EditorId?)null);

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
        var expectedOptions = new CloseDocumentOptions(ForceClose: true, SelectSuccessor: true);
        await _documentsService.Received(1).CloseDocument(new ResourceKey("notes/readme.md"), expectedOptions);
        await _utilityService.DidNotReceive().DockUtilityAsync(Arg.Any<EditorId>(), Arg.Any<DockLocation>());
    }

    [Test]
    public async Task ExecuteAsync_WithoutSuccessorSelection_CarriesTheOptionToTheService()
    {
        // Reopening with another editor closes and reopens the same document, so the neighbour must not
        // become active for the tick in between.
        var command = CreateCommand();
        command.FileResource = new ResourceKey("notes/readme.md");
        command.SelectSuccessor = false;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        var expectedOptions = new CloseDocumentOptions(ForceClose: false, SelectSuccessor: false);
        await _documentsService.Received(1).CloseDocument(new ResourceKey("notes/readme.md"), expectedOptions);
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

        await _utilityService.Received(1).DockUtilityAsync(NotepadUtilityId, DockLocation.UtilityPanel);
        await _documentsService.DidNotReceive().CloseDocument(Arg.Any<ResourceKey>(), Arg.Any<CloseDocumentOptions>());
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
        await _documentsService.DidNotReceive().CloseDocument(Arg.Any<ResourceKey>(), Arg.Any<CloseDocumentOptions>());
    }
}
