using System.Collections.ObjectModel;

namespace Celbridge.BaseLibrary.Resources;

/// <summary>
/// A data structure representing the resources in the project folder.
/// </summary>
public interface IResourceRegistry
{
    /// <summary>
    /// A folder resource containing the file and folder resources in the project.
    /// </summary>
    IFolderResource RootFolder { get; }

    /// <summary>
    /// Returns the resource key for a resource.
    /// </summary>
    ResourceKey GetResourceKey(IResource resource);

    /// <summary>
    /// Returns the absolute path for a resource.
    /// The path uses the directory separator character of the current platform.
    /// </summary>
    string GetPath(IResource resource);

    /// <summary>
    /// Returns the resource with the specified resource key.
    /// Fails if no matching resource is found.
    /// </summary>
    Result<IResource> GetResource(ResourceKey resourceKey);

    /// <summary>
    /// Updates the registry to mirror the current state of the files and folders in the project folder.
    /// </summary>
    Result UpdateResourceTree();

    /// <summary>
    /// Returns the list of expanded folders in the resource tree.
    /// </summary>
    public List<string> ExpandedFolders { get; }

    /// <summary>
    /// Mark a folder resource as expanded or collapsed in the resource tree.
    /// This does not affect the IsExpanded property of the folder resource itself.
    /// </summary>
    void SetFolderIsExpanded(ResourceKey resourceKey, bool isExpanded);

    /// <summary>
    /// Returns true if the folder with the specified resource key is expanded.
    /// </summary>
    public bool IsFolderExpanded(ResourceKey resourceKey);
}
