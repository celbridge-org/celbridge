using Celbridge.Commands;
using Celbridge.Resources;

namespace Celbridge.Projects;

/// <summary>
/// Delete a file or folder resource from the project.
/// </summary>
public interface IDeleteResourceCommand : IExecutableCommand
{
    /// <summary>
    /// Resource to delete.
    /// </summary>
    ResourceKey Resource { get; set; }
}
