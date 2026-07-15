using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.WorkspaceUI.ViewModels;

/// <summary>
/// Owns the Utility Panel rail state: the ordered rail items, which one is selected, and whether the selected
/// surface currently holds focus. The rail buttons bind to the per-item IsSelected/IsFocused/IsDocked flags,
/// so selection and focus updates flow through data binding rather than imperative button mutation.
/// </summary>
public partial class UtilityPanelViewModel : ObservableObject
{
    private readonly List<UtilityItemViewModel> _items = new();

    private UtilityId _selectedUtilityId = UtilityId.Empty;
    private WorkspacePanel _focusedPanel = WorkspacePanel.None;

    // True from a selection until focus lands on the selected surface. While it is true the accent is shown
    // optimistically: selecting a surface is a request to focus it, and the focus lands a beat later (after the
    // shown content is laid out). Rendering the accent immediately avoids a one-frame grey flash, and ignoring
    // focus reports for other panels while awaiting suppresses the transient bounce that occurs when the
    // outgoing panel is collapsed (WinUI briefly relocates focus off it before the new surface receives it).
    private bool _awaitingSelectionFocus;

    /// <summary>
    /// The rail items in display order: built-in surfaces first, then contributed utilities.
    /// </summary>
    public IReadOnlyList<UtilityItemViewModel> Items => _items;

    /// <summary>
    /// The utility id of the currently selected rail surface, or Empty when none is selected.
    /// </summary>
    public UtilityId SelectedUtilityId => _selectedUtilityId;

    /// <summary>
    /// Appends a rail item and returns its view model. focusIdentity is the workspace panel this surface
    /// reports focus as (WorkspacePanel.Utility for every contributed utility).
    /// </summary>
    public UtilityItemViewModel AddItem(UtilityId id, WorkspacePanel focusIdentity)
    {
        var item = new UtilityItemViewModel(id, focusIdentity);
        _items.Add(item);
        RefreshItemStates();

        return item;
    }

    /// <summary>
    /// Removes the rail item with the given id. A no-op when no item has that id.
    /// </summary>
    public void RemoveItem(UtilityId id)
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
    /// Selects the rail surface with the given id and shows the accent optimistically until focus settles on it.
    /// </summary>
    public void SelectUtility(UtilityId id)
    {
        _selectedUtilityId = id;
        _awaitingSelectionFocus = true;
        RefreshItemStates();
    }

    /// <summary>
    /// Reports the currently focused workspace panel so the accent can reflect real focus. While awaiting the
    /// selection's focus, a report for a different panel is ignored (the transient switch bounce); a report for
    /// the selected surface settles the wait.
    /// </summary>
    public void ReconcileFocus(WorkspacePanel focusedPanel)
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
    /// Sets whether the utility with the given id is docked as a document, which dims its rail button.
    /// </summary>
    public void SetDocked(UtilityId id, bool isDocked)
    {
        var item = FindItem(id);
        if (item is not null)
        {
            item.IsDocked = isDocked;
        }
    }

    private UtilityItemViewModel? FindItem(UtilityId id)
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

    private WorkspacePanel SelectedFocusIdentity
    {
        get
        {
            var selectedItem = FindItem(_selectedUtilityId);
            return selectedItem?.FocusIdentity ?? WorkspacePanel.None;
        }
    }

    // The selected surface counts as focused when we are optimistically awaiting its focus, or when the real
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
            return selectedFocusIdentity != WorkspacePanel.None
                && _focusedPanel == selectedFocusIdentity;
        }
    }

    private void RefreshItemStates()
    {
        var surfaceHasFocus = SelectedSurfaceHasFocus;

        foreach (var item in _items)
        {
            item.IsSelected = item.Id == _selectedUtilityId;
            item.IsFocused = item.IsSelected && surfaceHasFocus;
        }
    }
}
