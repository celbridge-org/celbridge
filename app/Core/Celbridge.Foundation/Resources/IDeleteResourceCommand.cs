using Celbridge.Commands;

namespace Celbridge.Resources;

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
