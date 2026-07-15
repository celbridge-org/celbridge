using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.WorkspaceUI.ViewModels;

/// <summary>
/// View model for a single Utility Panel rail item (a built-in Explorer/Search surface or a contributed
/// utility). The rail button binds its visual state to IsSelected, IsFocused, and IsDocked; the owning
/// UtilityPanelViewModel is the only writer of those properties.
/// </summary>
public partial class UtilityItemViewModel : ObservableObject
{
    /// <summary>
    /// The utility id this rail item represents.
    /// </summary>
    public UtilityId Id { get; }

    /// <summary>
    /// The workspace panel identity used to decide whether this surface currently holds focus. Built-in
    /// Explorer and Search have their own identities; every contributed utility reports as Utility.
    /// </summary>
    public WorkspacePanel FocusIdentity { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isFocused;

    [ObservableProperty]
    private bool _isDocked;

    public UtilityItemViewModel(UtilityId id, WorkspacePanel focusIdentity)
    {
        Id = id;
        FocusIdentity = focusIdentity;
    }
}
