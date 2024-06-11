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
    /// Scans the project folder for resources.
    /// </summary>
    Result ScanResources();
}
