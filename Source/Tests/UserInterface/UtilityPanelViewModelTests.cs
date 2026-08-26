using Celbridge.Workspace;
using Celbridge.WorkspaceUI.ViewModels;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for UtilityPanelViewModel, the rail mark state machine. Both marks describe the Utility Panel,
/// so the cases are: which item the panel is showing, whether the keyboard is in it (optimistic on selection,
/// suppressing the transient switch bounce, honouring real focus once it settles), and the items that carry
/// no mark at all.
/// </summary>
[TestFixture]
public class UtilityPanelViewModelTests
{
    private static readonly EditorId NotepadUtilityId = EditorId.Create("acme", "notepad");

    private UtilityPanelViewModel _viewModel = null!;
    private UtilityItemViewModel _explorer = null!;
    private UtilityItemViewModel _search = null!;

    [SetUp]
    public void SetUp()
    {
        _viewModel = new UtilityPanelViewModel();
        _explorer = _viewModel.AddItem(BuiltInUtilityIds.Explorer, WorkspacePanelId.Explorer);
        _search = _viewModel.AddItem(BuiltInUtilityIds.Search, WorkspacePanelId.Search);
    }

    [Test]
    public void AddItem_StartsUnselectedAndUnfocused()
    {
        _explorer.IsSelected.Should().BeFalse();
        _explorer.IsFocused.Should().BeFalse();
        _search.IsSelected.Should().BeFalse();
        _search.IsFocused.Should().BeFalse();
    }

    [Test]
    public void SelectUtility_SelectsTargetAndOptimisticallyFocusesIt()
    {
        _viewModel.SelectUtility(BuiltInUtilityIds.Explorer);

        _viewModel.SelectedUtilityId.Should().Be(BuiltInUtilityIds.Explorer);

        // The accent lights immediately on selection, before any focus report, so there is no grey flash.
        _explorer.IsSelected.Should().BeTrue();
        _explorer.IsFocused.Should().BeTrue();

        _search.IsSelected.Should().BeFalse();
        _search.IsFocused.Should().BeFalse();
    }

    [Test]
    public void SelectUtility_MovesSelectionOffThePreviousItem()
    {
        _viewModel.SelectUtility(BuiltInUtilityIds.Explorer);
        _viewModel.SelectUtility(BuiltInUtilityIds.Search);

        _explorer.IsSelected.Should().BeFalse();
        _explorer.IsFocused.Should().BeFalse();
        _search.IsSelected.Should().BeTrue();
        _search.IsFocused.Should().BeTrue();
    }

    [Test]
    public void ReconcileFocus_TransientOtherPanelWhileAwaiting_KeepsAccentLit()
    {
        _viewModel.SelectUtility(BuiltInUtilityIds.Explorer);

        // The switch collapses the outgoing panel, so focus briefly relocates to another panel before the new
        // surface receives it. That transient report must not drop the accent.
        _viewModel.ReconcileFocus(WorkspacePanelId.Documents);

        _explorer.IsFocused.Should().BeTrue();
    }

    [Test]
    public void ReconcileFocus_TargetPanel_SettlesAndKeepsAccentLit()
    {
        _viewModel.SelectUtility(BuiltInUtilityIds.Explorer);

        _viewModel.ReconcileFocus(WorkspacePanelId.Explorer);

        _explorer.IsFocused.Should().BeTrue();
    }

    [Test]
    public void ReconcileFocus_OtherPanelAfterSettling_ClearsAccent()
    {
        _viewModel.SelectUtility(BuiltInUtilityIds.Explorer);

        // Focus lands on the selected surface (settles the wait), then the user moves focus into a document.
        _viewModel.ReconcileFocus(WorkspacePanelId.Explorer);
        _viewModel.ReconcileFocus(WorkspacePanelId.Documents);

        // The accent now honours real focus: the selected surface no longer holds it.
        _explorer.IsSelected.Should().BeTrue();
        _explorer.IsFocused.Should().BeFalse();
    }

    [Test]
    public void ReconcileFocus_ReturningToTheSelectedSurface_RelightsAccent()
    {
        _viewModel.SelectUtility(BuiltInUtilityIds.Explorer);
        _viewModel.ReconcileFocus(WorkspacePanelId.Explorer);
        _viewModel.ReconcileFocus(WorkspacePanelId.Documents);

        _viewModel.ReconcileFocus(WorkspacePanelId.Explorer);

        _explorer.IsFocused.Should().BeTrue();
    }

    [Test]
    public void CustomUtility_FocusReportedAsUtility_LightsAccent()
    {
        var notepad = _viewModel.AddItem(NotepadUtilityId, WorkspacePanelId.CustomUtility);

        _viewModel.SelectUtility(NotepadUtilityId);
        _viewModel.ReconcileFocus(WorkspacePanelId.CustomUtility);

        notepad.IsSelected.Should().BeTrue();
        notepad.IsFocused.Should().BeTrue();
        _explorer.IsFocused.Should().BeFalse();
    }

    [Test]
    public void SetDocked_MarksTheItemDocked()
    {
        var notepad = _viewModel.AddItem(NotepadUtilityId, WorkspacePanelId.CustomUtility);

        _viewModel.SetDocked(NotepadUtilityId, true);
        notepad.IsDocked.Should().BeTrue();

        _viewModel.SetDocked(NotepadUtilityId, false);
        notepad.IsDocked.Should().BeFalse();
    }

    [Test]
    public void DockedUtility_CarriesNoMark()
    {
        var notepad = _viewModel.AddItem(NotepadUtilityId, WorkspacePanelId.CustomUtility);
        _viewModel.SelectUtility(NotepadUtilityId);

        // Docking hands the utility to a document tab, so the panel's marks stop being its to carry, even
        // though it was the panel's utility a moment ago.
        _viewModel.SetDocked(NotepadUtilityId, true);

        notepad.IsSelected.Should().BeFalse();
        notepad.IsFocused.Should().BeFalse();
    }

    [Test]
    public void CollapsedPanel_ClearsEveryMarkAndKeepsTheSelection()
    {
        _viewModel.SelectUtility(BuiltInUtilityIds.Explorer);

        _viewModel.SetPanelVisible(false);

        // A collapsed panel is showing nothing, so there is nothing to mark. The selection survives for the
        // next time it opens.
        _explorer.IsSelected.Should().BeFalse();
        _explorer.IsFocused.Should().BeFalse();
        _viewModel.SelectedUtilityId.Should().Be(BuiltInUtilityIds.Explorer);
    }

    [Test]
    public void RevealedPanel_MarksItsUtilityAgain()
    {
        _viewModel.SelectUtility(BuiltInUtilityIds.Explorer);
        _viewModel.SetPanelVisible(false);

        _viewModel.SetPanelVisible(true);

        _explorer.IsSelected.Should().BeTrue();
        _search.IsSelected.Should().BeFalse();
    }

    [Test]
    public void SelectUtility_NotAwaitingFocus_MarksTheUtilityWithoutTheAccent()
    {
        // A selection that is not taking the keyboard, such as restoring the panel's utility while another
        // panel holds focus. Awaiting a focus that will never arrive would leave the accent stuck on.
        _viewModel.SelectUtility(BuiltInUtilityIds.Explorer, awaitFocus: false);

        _explorer.IsSelected.Should().BeTrue();
        _explorer.IsFocused.Should().BeFalse();
    }

    [Test]
    public void RemoveItem_RemovesItFromTheRail()
    {
        _viewModel.AddItem(NotepadUtilityId, WorkspacePanelId.CustomUtility);
        _viewModel.Items.Should().HaveCount(3);

        _viewModel.RemoveItem(NotepadUtilityId);

        _viewModel.Items.Should().HaveCount(2);
        _viewModel.Items.Should().NotContain(item => item.Id == NotepadUtilityId);
    }
}
