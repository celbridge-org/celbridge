using System.Collections.ObjectModel;

namespace Celbridge.BaseLibrary.Resources;

/// <summary>
/// A data structure representing the resources in the project folder.
/// </summary>
public interface IResourceRegistry
{
    /// <summary>
    /// An observable collection of the resources in the project folder.
    /// </summary>
    ObservableCollection<IResource> Resources { get; }

    /// <summary>
    /// Updates the registry with the current state of the resources in the project folder.
    /// </summary>
    Result UpdateRegistry();
}
