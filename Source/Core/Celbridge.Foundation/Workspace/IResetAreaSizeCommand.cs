using Celbridge.Commands;

namespace Celbridge.Workspace;

/// <summary>
/// Resets a workspace area to its default size.
/// </summary>
public interface IResetAreaSizeCommand : IExecutableCommand
{
    /// <summary>
    /// The area to reset to its default size.
    /// </summary>
    WorkspaceArea Area { get; set; }
}
