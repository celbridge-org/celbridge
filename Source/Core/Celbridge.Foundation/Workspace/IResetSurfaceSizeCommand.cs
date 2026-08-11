using Celbridge.Commands;

namespace Celbridge.Workspace;

/// <summary>
/// Resets a workspace surface to its default size.
/// </summary>
public interface IResetSurfaceSizeCommand : IExecutableCommand
{
    /// <summary>
    /// The surface to reset to its default size.
    /// </summary>
    WorkspaceSurface Surface { get; set; }
}
