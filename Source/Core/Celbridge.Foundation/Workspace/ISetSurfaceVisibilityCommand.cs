using Celbridge.Commands;

namespace Celbridge.Workspace;

/// <summary>
/// Sets the visibility of workspace panel surfaces.
/// </summary>
public interface ISetSurfaceVisibilityCommand : IExecutableCommand
{
    /// <summary>
    /// Surface bitmask indicating which surfaces to show/hide.
    /// </summary>
    WorkspaceSurface Surfaces { get; set; }

    /// <summary>
    /// Whether to show or hide the specified surfaces.
    /// </summary>
    bool IsVisible { get; set; }
}
