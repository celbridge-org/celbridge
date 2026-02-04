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
    /// </summary>
    public Thickness IndentMargin => new Thickness(IndentLevel * 20, 0, 0, 0);

    /// <summary>
    /// The name of the resource for display.
    /// </summary>
    public string Name => Resource.Name;

    /// <summary>
    /// Creates a new ResourceViewItem for the given resource.
    /// </summary>
    public ResourceViewItem(IResource resource, int indentLevel, bool isExpanded, bool hasChildren)
    {
        Resource = resource;
        IndentLevel = indentLevel;
        _isExpanded = isExpanded;
        HasChildren = hasChildren;
    }
}
