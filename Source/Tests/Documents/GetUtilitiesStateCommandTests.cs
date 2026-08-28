using Celbridge.Documents.Commands;
using Celbridge.Packages;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers GetUtilitiesStateCommand's projection of the rail register: the order it reports, where it reads
/// each item's area from, and which item it reports as shown.
/// </summary>
[TestFixture]
public class GetUtilitiesStateCommandTests
{
    private static readonly EditorId NotepadId = EditorId.Create("acme", "notepad");
    private static readonly ResourceKey NotepadResource = new("utils:acme.notepad._notepad");
    private static readonly ResourceKey WorkshopResource = new("temp:workshop.webview");

    private IUtilityPanel _utilityPanel = null!;
    private IUtilityService _utilityService = null!;
    private IDocumentsService _documentsService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _utilityPanel = Substitute.For<IUtilityPanel>();
        _utilityPanel.ActiveUtilityId.Returns(BuiltInUtilityIds.Explorer);

        _utilityService = Substitute.For<IUtilityService>();
        _utilityService.GetRailItems().Returns(BuildRegister());

        // Nothing has moved, so every item is where its descriptor puts it.
        _utilityService.GetItemArea(Arg.Any<EditorId>()).Returns(WorkspaceArea.Utility);
        _utilityService.GetItemArea(BuiltInLauncherIds.Workshop).Returns(WorkspaceArea.Main);

        _documentsService = Substitute.For<IDocumentsService>();
        _documentsService.ActiveDocument.Returns(ResourceKey.Empty);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.UtilityPanel.Returns(_utilityPanel);
        workspaceService.UtilityService.Returns(_utilityService);
        workspaceService.DocumentsService.Returns(_documentsService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    // A register in rail order: a surface with no file behind it, a contribution utility, then a launcher.
    private static List<UtilityRailItem> BuildRegister()
    {
        return new List<UtilityRailItem>
        {
            new()
            {
                ItemId = BuiltInUtilityIds.Explorer,
                DisplayName = "Explorer",
                PanelView = new UtilityRailPanelView(new object(), () => { }, FocusPanelId.Explorer)
            },
            new()
            {
                ItemId = NotepadId,
                DisplayName = "Notepad",
                AllowedAreas = [WorkspaceArea.Utility, WorkspaceArea.Bottom],
                Resource = new UtilityRailResource(NotepadResource, NotepadId),
                PanelView = new UtilityRailPanelView(new object(), () => { }, FocusPanelId.CustomUtility)
            },
            new()
            {
                ItemId = BuiltInLauncherIds.Workshop,
                DisplayName = "Community Workshop",
                AllowedAreas = [WorkspaceArea.Main],
                DefaultArea = WorkspaceArea.Main,
                Resource = new UtilityRailResource(WorkshopResource, BuiltInEditors.WebViewEditorId)
            }
        };
    }

    [Test]
    public async Task Execute_ReportsTheRegisterInRailOrderWithItsResources()
    {
        var command = new GetUtilitiesStateCommand(_workspaceWrapper);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var utilities = command.ResultValue.Utilities;
        utilities.Should().HaveCount(3);

        utilities[0].UtilityId.Should().Be(BuiltInUtilityIds.Explorer);
        utilities[0].DisplayName.Should().Be("Explorer");
        utilities[0].Area.Should().Be(WorkspaceArea.Utility);

        // Explorer has no file behind it, so it reports no resource.
        utilities[0].Resource.IsEmpty.Should().BeTrue();

        utilities[1].UtilityId.Should().Be(NotepadId);
        utilities[1].Resource.Should().Be(NotepadResource);

        // A launcher is listed like anything else on the rail, whatever its scope.
        utilities[2].UtilityId.Should().Be(BuiltInLauncherIds.Workshop);
        utilities[2].DisplayName.Should().Be("Community Workshop");
        utilities[2].Area.Should().Be(WorkspaceArea.Main);
        utilities[2].Resource.Should().Be(WorkshopResource);
    }

    [Test]
    public async Task Execute_ReportsWhatEachItemDeclares()
    {
        // The declared set is reported alongside the current area, so a caller learns where an item may go
        // without attempting a move to find out.
        var command = new GetUtilitiesStateCommand(_workspaceWrapper);

        await command.ExecuteAsync();

        var utilities = command.ResultValue.Utilities;

        utilities.Single(utility => utility.UtilityId == NotepadId).AllowedAreas
            .Should().Equal(WorkspaceArea.Utility, WorkspaceArea.Bottom);

        utilities.Single(utility => utility.UtilityId == BuiltInLauncherIds.Workshop).AllowedAreas
            .Should().Equal(WorkspaceArea.Main);
    }

    [Test]
    public async Task Execute_ItemInThePanel_IsShownWhenTheRailHasSelectedIt()
    {
        _utilityPanel.ActiveUtilityId.Returns(NotepadId);

        var command = new GetUtilitiesStateCommand(_workspaceWrapper);

        await command.ExecuteAsync();

        var utilities = command.ResultValue.Utilities;
        utilities.Single(utility => utility.UtilityId == NotepadId).IsShown.Should().BeTrue();
        utilities.Single(utility => utility.UtilityId == BuiltInUtilityIds.Explorer).IsShown.Should().BeFalse();
    }

    [Test]
    public async Task Execute_ItemInADocumentArea_IsShownWhenItIsTheActiveDocument()
    {
        // The utility has been docked, so its area comes from the register rather than its descriptor, and it
        // is shown by being the active document rather than by the rail selection.
        _utilityService.GetItemArea(NotepadId).Returns(WorkspaceArea.Main);
        _documentsService.ActiveDocument.Returns(NotepadResource);

        var command = new GetUtilitiesStateCommand(_workspaceWrapper);

        await command.ExecuteAsync();

        var notepad = command.ResultValue.Utilities.Single(utility => utility.UtilityId == NotepadId);
        notepad.Area.Should().Be(WorkspaceArea.Main);
        notepad.IsShown.Should().BeTrue();

        // The Workshop is in a document area too, but its document is not the active one.
        var workshop = command.ResultValue.Utilities.Single(utility => utility.UtilityId == BuiltInLauncherIds.Workshop);
        workshop.IsShown.Should().BeFalse();
    }

    [Test]
    public async Task Execute_NoWorkspaceLoaded_ReturnsEmptyList()
    {
        _workspaceWrapper.IsWorkspaceLoaded.Returns(false);

        var command = new GetUtilitiesStateCommand(_workspaceWrapper);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.Utilities.Should().BeEmpty();
    }
}
