using Celbridge.Documents;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.WorkspaceUI.ViewModels;

/// <summary>
/// View model for a single Utility Panel rail item: a built-in Explorer or Search surface, or a custom
/// utility.
/// </summary>
public partial class UtilityItemViewModel : ObservableObject
{
    /// <summary>
    /// The utility id this rail item represents.
    /// </summary>
    public EditorId Id { get; }

    /// <summary>
    /// The workspace panel identity used to decide whether this surface currently holds focus. Built-in
    /// Explorer and Search have their own identities. Every custom utility reports as CustomUtility.
    /// </summary>
    public WorkspacePanelId FocusIdentity { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isFocused;

    /// <summary>
    /// Whether this utility is presented in a document tab rather than in the Utility Panel.
    /// </summary>
    public bool IsDocked { get; set; }

    public UtilityItemViewModel(EditorId id, WorkspacePanelId focusIdentity)
    {
        Id = id;
        FocusIdentity = focusIdentity;
    }
}
