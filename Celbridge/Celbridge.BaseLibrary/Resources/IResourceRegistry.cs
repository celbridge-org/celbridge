using System.Collections.ObjectModel;

namespace Celbridge.BaseLibrary.Resources;

/// <summary>
/// A data structure representing the resources in the project folder.
/// </summary>
public interface IResourceRegistry
{
    /// <summary>
    /// An observable collection representing the resources in the project folder.
    /// </summary>
    ObservableCollection<IResource> Resources { get; }

    /// <summary>
    /// Returns the project relative path for a resource.
    /// </summary>
    string GetResourcePath(IResource resource);

    /// <summary>
    /// Returns the resource corresponding a project relative path.
    /// Fails if no matching resource is found.
    /// </summary>
    Result<IResource> GetResourceAtPath(string folderPath);

    /// <summary>
    /// Updates the registry to mirror the current state of the resources in the project folder.
    /// </summary>
    Result UpdateRegistry();

    /// <summary>
    /// Returns a list of folders which the user has expanded in the resource tree view.
    /// </summary>
    List<String> GetExpandedFolders();

    /// <summary>
    /// Expands the specified folders.
    /// Any folders that are not present in the registry are ignored.
    /// </summary>
    void SetExpandedFolders(List<string> expandedFolders);

    /// <summary>
    /// Returns true if the folder resource at the specified path is expanded.
    /// </summary>
    public bool IsFolderExpanded(string folderPath);
}
