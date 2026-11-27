namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// Decides how the resource picker draws an item: whether the item is hidden, the item's writable state, and the
/// reason the item is read-only.
/// </summary>
public record ResourcePickerItemState(bool IsHidden, WritableState WritableState, string ReadOnlyMessage);

/// <summary>
/// Represents a single file or folder in the resource picker's list. A folder's listing shows each item by its name.
/// A search result also shows the item's full path, with a mark on the part of the path that matched the search.
/// </summary>
public class ResourcePickerItem
{
    private readonly bool _showsPath;

    public ResourceKey Resource { get; }

    public bool IsFolder { get; }

    /// <summary>
    /// The icon for a file's type. Null for a folder, which shows the folder icon.
    /// </summary>
    public IconDefinition? FileIcon { get; }

    /// <summary>
    /// The item's name, for example "photo.png".
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// A search result's path, split around the part that matched the search. The whole path is in Before when
    /// nothing is marked. Every part is empty in a folder's listing.
    /// </summary>
    public MarkedText MarkedPath { get; }

    /// <summary>
    /// Shows the path for a search result, and hides it in a folder's listing.
    /// </summary>
    public Visibility PathVisibility => _showsPath
        ? Visibility.Visible
        : Visibility.Collapsed;

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
    /// Returns true if the row is dimmed. Read-only and hidden resources both dim.
    /// </summary>
    public bool IsDimmed => IsReadOnly || IsHidden;

    /// <summary>
    /// The localized reason the resource is read-only. Empty when the resource is writable. Drives
    /// AutomationProperties.HelpText, so screen readers carry the same signal as the dimming.
    /// </summary>
    public string ReadOnlyMessage { get; }

    /// <summary>
    /// The tooltip shown when the user hovers the item. A search result shows its full path, since a long path can
    /// be cut short in the row. A read-only item also shows the read-only reason. A writable item in a folder's
    /// listing returns null, which leaves the tooltip element out.
    /// </summary>
    public string? TooltipText
    {
        get
        {
            var path = _showsPath
                ? Resource.Path
                : string.Empty;

            if (string.IsNullOrEmpty(ReadOnlyMessage))
            {
                return string.IsNullOrEmpty(path)
                    ? null
                    : path;
            }

            return string.IsNullOrEmpty(path)
                ? ReadOnlyMessage
                : $"{path}\n{ReadOnlyMessage}";
        }
    }

    public ResourcePickerItem(
        ResourceKey resource,
        bool isFolder,
        IconDefinition? fileIcon,
        ResourcePickerItemState state,
        bool showsPath)
    {
        Resource = resource;
        IsFolder = isFolder;
        FileIcon = fileIcon;
        Name = resource.ResourceName;
        MarkedPath = MarkedText.Unmarked(showsPath
            ? resource.Path
            : string.Empty);
        PathLower = resource.Path.ToLowerInvariant();
        WritableState = state.WritableState;
        IsHidden = state.IsHidden;
        ReadOnlyMessage = state.ReadOnlyMessage;
        _showsPath = showsPath;
    }

    // Copies an item, and marks part of the copy's path.
    private ResourcePickerItem(ResourcePickerItem item, int markStart, int markLength)
    {
        Resource = item.Resource;
        IsFolder = item.IsFolder;
        FileIcon = item.FileIcon;
        Name = item.Name;
        PathLower = item.PathLower;
        WritableState = item.WritableState;
        IsHidden = item.IsHidden;
        ReadOnlyMessage = item.ReadOnlyMessage;
        _showsPath = item._showsPath;
        MarkedPath = MarkedText.Create(item.Resource.Path, markStart, markLength);
    }

    /// <summary>
    /// Returns a copy of a search result with a mark on part of its path. The start and length count characters in
    /// the path. The search shares its items between searches, so each search marks copies.
    /// </summary>
    public ResourcePickerItem WithPathMark(int markStart, int markLength)
    {
        Guard.IsTrue(_showsPath);

        return new ResourcePickerItem(this, markStart, markLength);
    }

    /// <summary>
    /// Gives the list item its accessible name, since the list reads an item's name from ToString. A search result
    /// is named by its path, so two results with the same name can be told apart.
    /// </summary>
    public override string ToString() => _showsPath
        ? Resource.Path
        : Name;
}
