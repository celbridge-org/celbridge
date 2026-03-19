using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Delete one or more file or folder resources from the project.
/// </summary>
public interface IDeleteResourceCommand : IExecutableCommand
{
    /// <summary>
    /// Resources to delete.
    /// </summary>
    List<ResourceKey> Resources { get; set; }
}
