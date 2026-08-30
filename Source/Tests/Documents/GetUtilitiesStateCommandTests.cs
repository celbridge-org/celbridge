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
    private ILayoutService _layoutService = null!;

    [SetUp]
    public void Setup()
    {
        _utilityPanel = Substitute.For<IUtilityPanel>();
        _utilityPanel.ActiveUtilityId.Returns(BuiltInUtilityIds.Explorer);

        _utilityService = Substitute.For<IUtilityService>();
        _utilityService.GetRailItems().Returns(BuildRegister());

        // Nothing has moved, so every item is where its descriptor puts it.
        _utilityService.GetCurrentArea(Arg.Any<EditorId>()).Returns(WorkspaceArea.Utility);

        // Nothing is collapsed unless a test says so, so isVisible follows what is presenting the item.
        _layoutService = Substitute.For<ILayoutService>();
        _layoutService.IsAreaVisible(Arg.Any<WorkspaceArea>()).Returns(true);
        _utilityService.GetCurrentArea(BuiltInLauncherIds.Workshop).Returns(WorkspaceArea.Main);

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

    // A register in rail order: a panel utility with no file behind it, a dockable utility, then a launcher.
    private static List<UtilityRailItem> BuildRegister()
    {
        return new List<UtilityRailItem>
        {
            UtilityRailItem.CreatePanelUtility(
                BuiltInUtilityIds.Explorer, "explorer-utility-button", "folder", "Explorer", "Explorer",
                new UtilityRailPanelView(new object(), () => { }, FocusPanelId.Explorer)),

            UtilityRailItem.CreateDockableUtility(
                NotepadId, "notepad-utility-button", "sticky", "Notepad", "Notepad",
                NotepadResource,
                NotepadId,
                new UtilityRailPanelView(new object(), () => { }, FocusPanelId.CustomUtility),
                WorkspaceArea.Bottom),

            UtilityRailItem.CreateDocumentLauncher(
                BuiltInLauncherIds.Workshop, "workshop-utility-button", "people",
                "Community Workshop", "Community Workshop",
                WorkshopResource,
                BuiltInEditors.WebViewEditorId,
                WorkspaceArea.Main)
        };
    }

    [Test]
    public async Task Execute_ReportsTheRegisterInRailOrderWithItsResources()
    {
        var command = new GetUtilitiesStateCommand(_workspaceWrapper, _layoutService);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var utilities = command.ResultValue.Utilities;
        utilities.Should().HaveCount(3);

        utilities[0].UtilityId.Should().Be(BuiltInUtilityIds.Explorer);
        utilities[0].DisplayName.Should().Be("Explorer");
        utilities[0].CurrentArea.Should().Be(WorkspaceArea.Utility);

        // Explorer has no file behind it, so it reports no resource.
        utilities[0].Resource.IsEmpty.Should().BeTrue();

        utilities[1].UtilityId.Should().Be(NotepadId);
        utilities[1].Resource.Should().Be(NotepadResource);

        // A launcher is listed like anything else on the rail, whatever its scope.
        utilities[2].UtilityId.Should().Be(BuiltInLauncherIds.Workshop);
        utilities[2].DisplayName.Should().Be("Community Workshop");
        utilities[2].CurrentArea.Should().Be(WorkspaceArea.Main);
        utilities[2].Resource.Should().Be(WorkshopResource);
    }

    [Test]
    public async Task Execute_ReportsWhatEachItemDeclares()
    {
        // The declared set is reported alongside the current area, so a caller learns where an item may go
        // without attempting a move to find out.
        var command = new GetUtilitiesStateCommand(_workspaceWrapper, _layoutService);

        await command.ExecuteAsync();

        var utilities = command.ResultValue.Utilities;

        // A dockable utility reports where it docks to, a launcher where its document opens, and a panel
        // utility reports none because it never becomes a document.
        utilities.Single(utility => utility.UtilityId == NotepadId).DockArea
            .Should().Be(WorkspaceArea.Bottom);

        utilities.Single(utility => utility.UtilityId == BuiltInLauncherIds.Workshop).DockArea
            .Should().Be(WorkspaceArea.Main);

        utilities.Single(utility => utility.UtilityId == BuiltInUtilityIds.Explorer).DockArea
            .Should().BeNull();
    }

    [Test]
    public async Task Execute_ItemInThePanel_IsShownWhenTheRailHasSelectedIt()
    {
        _utilityPanel.ActiveUtilityId.Returns(NotepadId);

        var command = new GetUtilitiesStateCommand(_workspaceWrapper, _layoutService);

        await command.ExecuteAsync();

        var utilities = command.ResultValue.Utilities;
        utilities.Single(utility => utility.UtilityId == NotepadId).IsVisible.Should().BeTrue();
        utilities.Single(utility => utility.UtilityId == BuiltInUtilityIds.Explorer).IsVisible.Should().BeFalse();
    }

    [Test]
    public async Task Execute_ItemInADocumentArea_IsVisibleWhenItsSectionIsShowingIt()
    {
        // The utility has been docked, so its area comes from the register rather than its descriptor, and
        // its section showing it is what puts it on screen rather than the rail selection.
        _utilityService.GetCurrentArea(NotepadId).Returns(WorkspaceArea.Main);
        OpenDocumentInSection(NotepadResource, DocumentSection.MainLeft, isSelected: true);

        var command = new GetUtilitiesStateCommand(_workspaceWrapper, _layoutService);

        await command.ExecuteAsync();

        var notepad = command.ResultValue.Utilities.Single(utility => utility.UtilityId == NotepadId);
        notepad.CurrentArea.Should().Be(WorkspaceArea.Main);
        notepad.IsVisible.Should().BeTrue();

        // The Workshop is in a document area too, but no section is showing its document.
        var workshop = command.ResultValue.Utilities.Single(utility => utility.UtilityId == BuiltInLauncherIds.Workshop);
        workshop.IsVisible.Should().BeFalse();
    }

    [Test]
    public async Task Execute_ItemInAnotherAreaThanTheActiveDocument_IsStillVisible()
    {
        // Each section shows its own selected tab, so a utility docked in the Bottom area is on screen even
        // while the user is working in a document in Main.
        _utilityService.GetCurrentArea(NotepadId).Returns(WorkspaceArea.Bottom);
        OpenDocumentInSection(NotepadResource, DocumentSection.BottomLeft, isSelected: true);

        var otherDocument = new ResourceKey("notes/readme.md");
        _documentsService.ActiveDocument.Returns(otherDocument);
        OpenDocumentInSection(otherDocument, DocumentSection.MainLeft, isSelected: true);

        var command = new GetUtilitiesStateCommand(_workspaceWrapper, _layoutService);

        await command.ExecuteAsync();

        var notepad = command.ResultValue.Utilities.Single(utility => utility.UtilityId == NotepadId);
        notepad.IsVisible.Should().BeTrue();
    }

    [Test]
    public async Task Execute_ItemBehindAnotherTabInItsSection_IsNotVisible()
    {
        // Open in the Bottom area, but another tab in that section is the one drawn.
        _utilityService.GetCurrentArea(NotepadId).Returns(WorkspaceArea.Bottom);
        OpenDocumentInSection(NotepadResource, DocumentSection.BottomLeft, isSelected: false);

        var command = new GetUtilitiesStateCommand(_workspaceWrapper, _layoutService);

        await command.ExecuteAsync();

        var notepad = command.ResultValue.Utilities.Single(utility => utility.UtilityId == NotepadId);
        notepad.IsVisible.Should().BeFalse();
    }

    // Reports a document as open in a section, and optionally as the tab that section is showing.
    private void OpenDocumentInSection(ResourceKey resource, DocumentSection section, bool isSelected)
    {
        var address = new DocumentAddress(WindowIndex: 0, Section: section, TabOrder: 0);
        _documentsService.FindOpenDocument(resource)
            .Returns(new OpenDocumentInfo(resource, address, EditorId.Empty));

        if (isSelected)
        {
            _documentsService.GetSelectedDocument(section).Returns(resource);
        }
    }

    [Test]
    public async Task Execute_NoWorkspaceLoaded_ReturnsEmptyList()
    {
        _workspaceWrapper.IsWorkspaceLoaded.Returns(false);

        var command = new GetUtilitiesStateCommand(_workspaceWrapper, _layoutService);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.Utilities.Should().BeEmpty();
    }

    [Test]
    public async Task Execute_SelectedInACollapsedArea_IsNotShown()
    {
        // The rail keeps its selection through a collapse, so the selected utility is still the active one.
        _utilityPanel.ActiveUtilityId.Returns(BuiltInUtilityIds.Explorer);
        _layoutService.IsAreaVisible(WorkspaceArea.Utility).Returns(false);

        var command = new GetUtilitiesStateCommand(_workspaceWrapper, _layoutService);

        await command.ExecuteAsync();

        // Selected, but the panel showing it is collapsed, so the user cannot see it.
        var explorer = command.ResultValue.Utilities.Single(
            utility => utility.UtilityId == BuiltInUtilityIds.Explorer);

        explorer.CurrentArea.Should().Be(WorkspaceArea.Utility);
        explorer.IsVisible.Should().BeFalse();
    }
}
