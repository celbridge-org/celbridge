namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// Decides how the resource picker draws an item: whether the item is hidden, the item's writable state, and the
/// reason the item is read-only.
/// </summary>
public record ResourcePickerItemState(bool IsHidden, WritableState WritableState, string ReadOnlyMessage);

/// <summary>
/// Represents a single file or folder in the resource picker's list. A folder's listing shows each item by its name.
/// A search result also shows the item's folder.
/// </summary>
public class ResourcePickerItem
{
    private readonly bool _showsFolder;

    public ResourceKey Resource { get; }

    public bool IsFolder { get; }

    public IconDefinition IconDefinition { get; }

    /// <summary>
    /// The item's name, for example "photo.png".
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Returns the path of the item's folder, which shows beside a search result's name. Empty in a folder's
    /// listing, and for an item in the project folder.
    /// </summary>
    public string FolderText { get; }

    /// <summary>
    /// Shows the folder text when the item has one.
    /// </summary>
    public Visibility FolderTextVisibility => string.IsNullOrEmpty(FolderText)
        ? Visibility.Collapsed
        : Visibility.Visible;

    /// <summary>
    /// The item's path in lowercase. The search matches typed text against this path.
    /// </summary>
    public string PathLower { get; }

    /// <summary>
    /// Returns the resource's writable state, which says whether the resource accepts edits and the reason when it
    /// does not.
    /// </summary>
    public WritableState WritableState { get; }

    /// <summary>
    /// Returns true if the resource refuses edits.
    /// </summary>
    public bool IsReadOnly => WritableState != WritableState.Writable;

    /// <summary>
    /// Returns true if the Explorer hides the resource while the Explorer is not showing hidden files.
    /// </summary>
    public bool IsHidden { get; }

    /// <summary>
    /// Returns true if the row is dimmed. Read-only and hidden resources both dim. The Explorer dims the same rows.
    /// </summary>
    public bool IsDimmed => IsReadOnly || IsHidden;

    /// <summary>
    /// The localized reason the resource is read-only. Empty when the resource is writable. Drives
    /// AutomationProperties.HelpText, so screen readers carry the same signal as the dimming.
    /// </summary>
    public string ReadOnlyMessage { get; }

    /// <summary>
    /// The tooltip shown when the user hovers the item. A read-only item shows the read-only reason. A writable item
    /// returns null, which leaves the tooltip element out.
    /// </summary>
    public string? TooltipText => string.IsNullOrEmpty(ReadOnlyMessage)
        ? null
        : ReadOnlyMessage;

    public ResourcePickerItem(
        ResourceKey resource,
        bool isFolder,
        IconDefinition iconDefinition,
        ResourcePickerItemState state,
        bool showsFolder)
    {
        Resource = resource;
        IsFolder = isFolder;
        IconDefinition = iconDefinition;
        Name = resource.ResourceName;
        FolderText = showsFolder
            ? resource.GetParent().Path
            : string.Empty;
        PathLower = resource.Path.ToLowerInvariant();
        WritableState = state.WritableState;
        IsHidden = state.IsHidden;
        ReadOnlyMessage = state.ReadOnlyMessage;
        _showsFolder = showsFolder;
    }

    /// <summary>
    /// Gives the list item its accessible name, since the list reads an item's name from ToString. A search result
    /// is named by its path, so two results with the same name can be told apart.
    /// </summary>
    public override string ToString() => _showsFolder
        ? Resource.Path
        : Name;
}
