using Celbridge.Commands;
using Celbridge.Resources;

namespace Celbridge.Projects;

/// <summary>
/// Add a file or folder resource to the project.
/// </summary>
public interface IAddResourceCommand : IExecutableCommand
{
    /// <summary>
    /// The type of resource to add
    /// </summary>
    ResourceType ResourceType { get; set; }

    /// <summary>
    /// Resource key for the new resource
    /// </summary>
    ResourceKey DestResource { get; set; }

    /// <summary>
    /// Path to copy the resource from.
    /// If empty, then an empty resource is created.
    /// </summary>
    string SourcePath { get; set; }
}
