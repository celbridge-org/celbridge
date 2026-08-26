using Celbridge.Documents;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.WorkspaceUI.ViewModels;

/// <summary>
/// Owns the Utility Panel rail state: the ordered rail items, which one the panel is showing, and whether
/// it holds the keyboard.
/// </summary>
public partial class UtilityPanelViewModel : ObservableObject
{
    private readonly List<UtilityItemViewModel> _items = new();

    private EditorId _selectedUtilityId = EditorId.Empty;
    private WorkspacePanelId _focusedPanel = WorkspacePanelId.None;
    private bool _isPanelVisible = true;

    // True from a selection until focus lands on the selected surface. While it is true the accent is shown
    // optimistically and focus reports for other panels are ignored, which suppresses the transient bounce as
    // the outgoing panel is collapsed (WinUI briefly relocates focus off it before the new surface receives it).
    private bool _awaitingSelectionFocus;

    /// <summary>
    /// The rail items in display order: built-in surfaces first, then custom utilities.
    /// </summary>
    public IReadOnlyList<UtilityItemViewModel> Items => _items;

    /// <summary>
    /// The utility id of the currently selected rail surface, or Empty when none is selected.
    /// </summary>
    public EditorId SelectedUtilityId => _selectedUtilityId;

    /// <summary>
    /// Appends a rail item and returns its view model. focusIdentity is the workspace panel this surface
    /// reports focus as (WorkspacePanelId.CustomUtility for every custom utility).
    /// </summary>
    public UtilityItemViewModel AddItem(EditorId id, WorkspacePanelId focusIdentity)
    {
        var item = new UtilityItemViewModel(id, focusIdentity);
        _items.Add(item);
        RefreshItemStates();

        return item;
    }

    /// <summary>
    /// Removes the rail item with the given id. A no-op when no item has that id.
    /// </summary>
    public void RemoveItem(EditorId id)
    {
        var item = FindItem(id);
        if (item is null)
        {
            return;
        }

        _items.Remove(item);
        RefreshItemStates();
    }

    /// <summary>
    /// Makes the utility with the given id the one the panel is showing. Pass awaitFocus when the surface is
    /// about to take the keyboard, so it counts as focused until focus settles on it.
    /// </summary>
    public void SelectUtility(EditorId id, bool awaitFocus = true)
    {
        _selectedUtilityId = id;
        _awaitingSelectionFocus = awaitFocus;
        RefreshItemStates();
    }

    /// <summary>
    /// Reports which workspace panel currently holds focus. While awaiting a selection's focus, a report for
    /// a different panel is ignored (the transient switch bounce). A report for the selected surface settles
    /// the wait.
    /// </summary>
    public void ReconcileFocus(WorkspacePanelId focusedPanel)
    {
        _focusedPanel = focusedPanel;

        if (_awaitingSelectionFocus
            && focusedPanel == SelectedFocusIdentity)
        {
            _awaitingSelectionFocus = false;
        }

        RefreshItemStates();
    }

    /// <summary>
    /// Sets whether the utility with the given id is presented in a document tab rather than by this panel.
    /// </summary>
    public void SetDocked(EditorId id, bool isDocked)
    {
        var item = FindItem(id);
        if (item is not null)
        {
            item.IsDocked = isDocked;
            RefreshItemStates();
        }
    }

    /// <summary>
    /// Sets whether the Utility Panel is on screen.
    /// </summary>
    public void SetPanelVisible(bool isVisible)
    {
        _isPanelVisible = isVisible;
        RefreshItemStates();
    }

    private UtilityItemViewModel? FindItem(EditorId id)
    {
        foreach (var item in _items)
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }

    private WorkspacePanelId SelectedFocusIdentity
    {
        get
        {
            var selectedItem = FindItem(_selectedUtilityId);
            return selectedItem?.FocusIdentity ?? WorkspacePanelId.None;
        }
    }

    // The shown utility counts as focused when we are optimistically awaiting its focus, or when the real
    // focused panel matches its identity.
    private bool SelectedSurfaceHasFocus
    {
        get
        {
            if (_awaitingSelectionFocus)
            {
                return true;
            }

            var selectedFocusIdentity = SelectedFocusIdentity;
            return selectedFocusIdentity != WorkspacePanelId.None
                && _focusedPanel == selectedFocusIdentity;
        }
    }

    private void RefreshItemStates()
    {
        var surfaceHasFocus = SelectedSurfaceHasFocus;

        foreach (var item in _items)
        {
            // A docked utility is presented in a document tab rather than by this panel, so the panel's marks
            // are not its to carry.
            item.IsSelected = _isPanelVisible
                && !item.IsDocked
                && item.Id == _selectedUtilityId;

            item.IsFocused = item.IsSelected && surfaceHasFocus;
        }
    }
}
