using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers the rail item factories: the kind a contributed utility takes from the dock area its manifest
/// declares, and the rail band each item is built into.
/// </summary>
[TestFixture]
public class UtilityRailItemTests
{
    private static readonly EditorId NotepadId = EditorId.Create("acme", "notepad");
    private static readonly ResourceKey NotepadResource = new("utils:acme.notepad._notepad");

    private static UtilityRailItem CreateContributedUtility(WorkspaceArea? dockArea)
    {
        var panelView = new UtilityRailPanelView(new object(), () => { }, FocusPanelId.CustomUtility);

        return UtilityRailItem.CreateContributedUtility(
            NotepadId,
            "notepad-utility-button",
            "sticky",
            "Notepad",
            "Notepad",
            NotepadResource,
            NotepadId,
            panelView,
            dockArea);
    }

    [Test]
    public void ContributedUtility_WithADeclaredArea_IsDockable()
    {
        var railItem = CreateContributedUtility(WorkspaceArea.Bottom);

        railItem.Kind.Should().Be(RailItemKind.DockableUtility);
        railItem.Group.Should().Be(RailItemGroup.ProjectItem);
        railItem.DockArea.Should().Be(WorkspaceArea.Bottom);

        // A dockable utility occupies the panel until it is docked, so it carries a panel view either way.
        railItem.PanelView.Should().NotBeNull();
        railItem.FileResource.Should().Be(NotepadResource);
        railItem.EditorId.Should().Be(NotepadId);
    }

    [Test]
    public void ContributedUtility_DeclaringNoArea_StaysInThePanel()
    {
        // A manifest saying dock-area = "none" parses to a null area, which is what makes the utility
        // undockable rather than a separate flag that could disagree with it.
        var railItem = CreateContributedUtility(null);

        railItem.Kind.Should().Be(RailItemKind.PanelUtility);
        railItem.Group.Should().Be(RailItemGroup.ProjectItem);
        railItem.DockArea.Should().BeNull();

        // It still owns a state file, unlike Explorer and Search, which have none.
        railItem.PanelView.Should().NotBeNull();
        railItem.FileResource.Should().Be(NotepadResource);
        railItem.EditorId.Should().Be(NotepadId);
    }

    [Test]
    public void BuiltInPanelUtility_CarriesNoFile()
    {
        var panelView = new UtilityRailPanelView(new object(), () => { }, FocusPanelId.Explorer);

        var railItem = UtilityRailItem.CreatePanelUtility(
            RailItemGroup.BuiltInUtility,
            BuiltInUtilityIds.Explorer, "explorer-utility-button", "folder", "Explorer", "Explorer", panelView);

        railItem.Kind.Should().Be(RailItemKind.PanelUtility);
        railItem.Group.Should().Be(RailItemGroup.BuiltInUtility);
        railItem.DockArea.Should().BeNull();
        railItem.FileResource.IsEmpty.Should().BeTrue();
        railItem.EditorId.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void DocumentShortcut_CarriesNoPanelView()
    {
        var railItem = UtilityRailItem.CreateDocumentShortcut(
            RailItemGroup.BuiltInShortcut,
            BuiltInShortcutIds.Community,
            "community-utility-button",
            "people",
            "Community",
            "Community",
            new ResourceKey("temp:community.webview"),
            EditorId.Create("celbridge", "webview"),
            WorkspaceArea.Main);

        railItem.Kind.Should().Be(RailItemKind.DocumentShortcut);
        railItem.Group.Should().Be(RailItemGroup.BuiltInShortcut);
        railItem.DockArea.Should().Be(WorkspaceArea.Main);

        // A document shortcut never occupies the panel, so it parks no live view there.
        railItem.PanelView.Should().BeNull();
    }
}
