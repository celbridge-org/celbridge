using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Create a file or folder resource in the project.
/// </summary>
public interface ICreateResourceCommand : IExecutableCommand
{
    /// <summary>
    /// The type of resource to create
    /// </summary>
    ResourceType ResourceType { get; set; }

    /// <summary>
    /// Path to copy the resource from.
    /// If empty, then an empty resource is created.
    /// </summary>
    string SourcePath { get; set; }

    /// <summary>
    /// Resource key for the new resource
    /// </summary>
    ResourceKey DestResource { get; set; }

    /// <summary>
    /// Open the created file in the Explorer after it's been added to the project.
    /// </summary>
    bool OpenAfterCreating { get; set; }
}
