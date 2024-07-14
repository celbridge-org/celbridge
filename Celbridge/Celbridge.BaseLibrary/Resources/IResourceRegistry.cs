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
    /// Returns the resource path for a resource.
    /// </summary>
    string GetResourcePath(IResource resource);

    /// <summary>
    /// Returns the absolute path for a resource.
    /// The path uses the directory separator character of the current platform.
    /// </summary>
    string GetPath(IResource resource);

    /// <summary>
    /// Returns the resource at the specified resource path.
    /// Fails if no matching resource is found.
    /// </summary>
    Result<IResource> GetResource(string resourcePath);

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
    void SetFolderIsExpanded(string resourcePath, bool isExpanded);

    /// <summary>
    /// Returns true if the folder at the specified resource path is expanded.
    /// </summary>
    public bool IsFolderExpanded(string resourcePath);
}
