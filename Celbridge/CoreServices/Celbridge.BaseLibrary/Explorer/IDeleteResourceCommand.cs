using Celbridge.Commands;

namespace Celbridge.Explorer;

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
