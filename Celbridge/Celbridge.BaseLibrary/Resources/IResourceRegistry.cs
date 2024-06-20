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
}
