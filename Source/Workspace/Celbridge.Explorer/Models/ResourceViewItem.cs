using Celbridge.UserInterface;

namespace Celbridge.Explorer.Models;

/// <summary>
/// Everything a resource row draws from, as a value. Two rows with equal appearances render
/// identically.
/// </summary>
public readonly record struct ResourceViewItemAppearance(
    string Name,
    int IndentLevel,
    bool IsFolder,
    bool HasChildren,
    bool IsExpanded,
    bool IsProjectFolder,
    bool IsHidden,
    WritableState WritableState,
    string ReadOnlyMessage,
    string ProjectFolderTooltip);

/// <summary>
/// Represents a single resource item in the resource view. A row never changes. When a resource's
/// appearance changes, the resource gets a new row.
/// </summary>
public sealed class ResourceViewItem
{
    public ResourceViewItem(
        ResourceKey resource,
        ResourceViewItemAppearance appearance,
        IconDefinition? fileIcon)
    {
        Resource = resource;
        Appearance = appearance;
        FileIcon = fileIcon;
    }

    /// <summary>
    /// The key of the file or folder this item represents.
    /// </summary>
    public ResourceKey Resource { get; }

    /// <summary>
    /// The values this row renders from.
    /// </summary>
    public ResourceViewItemAppearance Appearance { get; }

    /// <summary>
    /// The icon drawn for a file. Null for a folder, which draws the folder icon.
    /// </summary>
    public IconDefinition? FileIcon { get; }

    /// <summary>
    /// The resource and whether it is a folder, for menus and drags.
    /// </summary>
    public ResourceItem Item => new(Resource, IsFolder);

    /// <summary>
    /// The name of the resource for display.
    /// For the project folder, this returns the project folder name.
    /// </summary>
    public string Name => Appearance.Name;

    /// <summary>
    /// The depth level in the tree hierarchy (0 = root level).
    /// </summary>
    public int IndentLevel => Appearance.IndentLevel;

    /// <summary>
    /// Whether this item is a folder resource.
    /// </summary>
    public bool IsFolder => Appearance.IsFolder;

    /// <summary>
    /// Whether this item has children that can be expanded.
    /// </summary>
    public bool HasChildren => Appearance.HasChildren;

    /// <summary>
    /// Whether this folder item is currently expanded.
    /// Only applicable for folder resources.
    /// </summary>
    public bool IsExpanded => Appearance.IsExpanded;

    /// <summary>
    /// Whether this item is the project folder.
    /// The project folder has special handling.
    /// </summary>
    public bool IsProjectFolder => Appearance.IsProjectFolder;

    /// <summary>
    /// Returns true if the tree hides the resource by default. The row is then on screen only
    /// because Show Hidden Files is on.
    /// </summary>
    public bool IsHidden => Appearance.IsHidden;

    /// <summary>
    /// The writable state of the resource, read from its folder's listing.
    /// </summary>
    public WritableState WritableState => Appearance.WritableState;

    /// <summary>
    /// Localised explanation of why the resource is read-only. Empty when
    /// writable. Drives AutomationProperties.HelpText so screen readers carry
    /// the same signal as the visual dimming.
    /// </summary>
    public string ReadOnlyMessage => Appearance.ReadOnlyMessage;

    /// <summary>
    /// Localised affordance hint shown when hovering the project folder, naming
    /// the platform's file manager. Empty for non-project-folder items.
    /// </summary>
    public string ProjectFolderTooltip => Appearance.ProjectFolderTooltip;

    /// <summary>
    /// The margin used for visual indentation based on tree depth.
    /// Project folder gets negative margin to align with left edge of panel.
    /// </summary>
    public Thickness IndentMargin => IsProjectFolder
        ? new Thickness(-32, 0, 0, 0)  // Shift project folder left to align with panel edge
        : new Thickness(IndentLevel * 20, 0, 0, 0);

    /// <summary>
    /// Visibility for the expand/collapse chevron.
    /// Hidden for the project folder and files.
    /// </summary>
    public Visibility ChevronVisibility =>
        IsProjectFolder ? Visibility.Collapsed : (HasChildren ? Visibility.Visible : Visibility.Collapsed);

    /// <summary>
    /// Returns true if the resource refuses edits. Drives the dimming binding and the
    /// visibility of the read-only tooltip.
    /// </summary>
    public bool IsReadOnly => WritableState != WritableState.Writable;

    /// <summary>
    /// Returns true if the row is drawn set apart from the rest of the tree. Read-only and
    /// hidden resources both dim, which is the same signal for both: the row is
    /// there but is not one you are working on.
    /// </summary>
    public bool IsDimmed => IsReadOnly || IsHidden;

    /// <summary>
    /// The tooltip shown when the user hovers the item. The project folder
    /// retains its existing affordance hint; non-editable items show the
    /// read-only reason; editable items have no tooltip (returns null so the
    /// tooltip element does not render).
    /// </summary>
    public string? TooltipText
    {
        get
        {
            if (IsProjectFolder)
            {
                return ProjectFolderTooltip;
            }

            return string.IsNullOrEmpty(ReadOnlyMessage)
                ? null
                : ReadOnlyMessage;
        }
    }

    /// <summary>
    /// The resource's name. The ListView names each row to screen readers by its item's string form.
    /// </summary>
    public override string ToString()
    {
        return Name;
    }
}
