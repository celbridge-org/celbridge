using Celbridge.Workspace;
using Celbridge.WorkspaceUI.ViewModels;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for UtilityPanelViewModel, the rail selection/focus state machine. Focus on the accent rules:
/// optimistic focus on selection, suppression of the transient switch bounce, and honouring real focus once
/// the selection settles.
/// </summary>
[TestFixture]
public class UtilityPanelViewModelTests
{
    private static readonly EditorInstanceId NotepadUtilityId = EditorInstanceId.Create("acme", "notepad");

    private UtilityPanelViewModel _viewModel = null!;
    private UtilityItemViewModel _explorer = null!;
    private UtilityItemViewModel _search = null!;

    [SetUp]
    public void SetUp()
    {
        _viewModel = new UtilityPanelViewModel();
        _explorer = _viewModel.AddItem(BuiltInUtilityIds.Explorer, WorkspacePanel.Explorer);
        _search = _viewModel.AddItem(BuiltInUtilityIds.Search, WorkspacePanel.Search);
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
        _viewModel.ReconcileFocus(WorkspacePanel.Documents);

        _explorer.IsFocused.Should().BeTrue();
    }

    [Test]
    public void ReconcileFocus_TargetPanel_SettlesAndKeepsAccentLit()
    {
        _viewModel.SelectUtility(BuiltInUtilityIds.Explorer);

        _viewModel.ReconcileFocus(WorkspacePanel.Explorer);

        _explorer.IsFocused.Should().BeTrue();
    }

    [Test]
    public void ReconcileFocus_OtherPanelAfterSettling_ClearsAccent()
    {
        _viewModel.SelectUtility(BuiltInUtilityIds.Explorer);

        // Focus lands on the selected surface (settles the wait), then the user moves focus into a document.
        _viewModel.ReconcileFocus(WorkspacePanel.Explorer);
        _viewModel.ReconcileFocus(WorkspacePanel.Documents);

        // The accent now honours real focus: the selected surface no longer holds it.
        _explorer.IsSelected.Should().BeTrue();
        _explorer.IsFocused.Should().BeFalse();
    }

    [Test]
    public void ReconcileFocus_ReturningToTheSelectedSurface_RelightsAccent()
    {
        _viewModel.SelectUtility(BuiltInUtilityIds.Explorer);
        _viewModel.ReconcileFocus(WorkspacePanel.Explorer);
        _viewModel.ReconcileFocus(WorkspacePanel.Documents);

        _viewModel.ReconcileFocus(WorkspacePanel.Explorer);

        _explorer.IsFocused.Should().BeTrue();
    }

    [Test]
    public void CustomUtility_FocusReportedAsUtility_LightsAccent()
    {
        var notepad = _viewModel.AddItem(NotepadUtilityId, WorkspacePanel.CustomUtility);

        _viewModel.SelectUtility(NotepadUtilityId);
        _viewModel.ReconcileFocus(WorkspacePanel.CustomUtility);

        notepad.IsSelected.Should().BeTrue();
        notepad.IsFocused.Should().BeTrue();
        _explorer.IsFocused.Should().BeFalse();
    }

    [Test]
    public void SetDocked_MarksTheItemDocked()
    {
        var notepad = _viewModel.AddItem(NotepadUtilityId, WorkspacePanel.CustomUtility);

        _viewModel.SetDocked(NotepadUtilityId, true);
        notepad.IsDocked.Should().BeTrue();

        _viewModel.SetDocked(NotepadUtilityId, false);
        notepad.IsDocked.Should().BeFalse();
    }

    [Test]
    public void RemoveItem_RemovesItFromTheRail()
    {
        _viewModel.AddItem(NotepadUtilityId, WorkspacePanel.CustomUtility);
        _viewModel.Items.Should().HaveCount(3);

        _viewModel.RemoveItem(NotepadUtilityId);

        _viewModel.Items.Should().HaveCount(2);
        _viewModel.Items.Should().NotContain(item => item.Id == NotepadUtilityId);
    }
}
