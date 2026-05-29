using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Explorer.Models;

/// <summary>
/// Represents a single resource item in the resource view.
/// </summary>
public partial class ResourceViewItem : ObservableObject
{
    /// <summary>
    /// The resource (file or folder) this item represents.
    /// </summary>
    public IResource Resource { get; }

    /// <summary>
    /// The depth level in the tree hierarchy (0 = root level).
    /// </summary>
    public int IndentLevel { get; }

    /// <summary>
    /// Whether this folder item is currently expanded.
    /// Only applicable for folder resources.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Whether this item has children that can be expanded.
    /// </summary>
    public bool HasChildren { get; }

    /// <summary>
    /// Whether this item is a folder resource.
    /// </summary>
    public bool IsFolder => Resource is IFolderResource;

    /// <summary>
    /// The margin used for visual indentation based on tree depth.
    /// Project folder gets negative margin to align with left edge of panel.
    /// </summary>
    public Thickness IndentMargin => IsProjectFolder
        ? new Thickness(-32, 0, 0, 0)  // Shift project folder left to align with panel edge
        : new Thickness(IndentLevel * 20, 0, 0, 0);

    /// <summary>
    /// The name of the resource for display.
    /// For the project folder, this returns the project folder name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Whether this item is the project folder.
    /// The project folder has special handling.
    /// </summary>
    public bool IsProjectFolder { get; }

    /// <summary>
    /// Visibility for the expand/collapse chevron.
    /// Hidden for the project folder and files.
    /// </summary>
    public Visibility ChevronVisibility =>
        IsProjectFolder ? Visibility.Collapsed : (HasChildren ? Visibility.Visible : Visibility.Collapsed);

    /// <summary>
    /// Creates a new ResourceViewItem for the given resource.
    /// </summary>
    public ResourceViewItem(IResource resource, int indentLevel, bool isExpanded, bool hasChildren, bool isProjectFolder = false, string? displayName = null)
    {
        Resource = resource;
        IndentLevel = indentLevel;
        _isExpanded = isExpanded;
        HasChildren = hasChildren;
        IsProjectFolder = isProjectFolder;
        Name = displayName ?? resource.Name;
    }
}
