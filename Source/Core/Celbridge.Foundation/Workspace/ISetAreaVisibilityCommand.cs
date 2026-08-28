using Celbridge.Commands;

namespace Celbridge.Workspace;

/// <summary>
/// Shows or hides a workspace area.
/// </summary>
public interface ISetAreaVisibilityCommand : IExecutableCommand
{
    /// <summary>
    /// The area to show or hide.
    /// </summary>
    WorkspaceArea Area { get; set; }

    /// <summary>
    /// Whether to show or hide the area.
    /// </summary>
    bool IsVisible { get; set; }
}
