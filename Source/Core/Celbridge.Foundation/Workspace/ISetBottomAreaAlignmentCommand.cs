using Celbridge.Commands;

namespace Celbridge.Workspace;

/// <summary>
/// Sets how far the Bottom document area spans across the workspace.
/// </summary>
public interface ISetBottomAreaAlignmentCommand : IExecutableCommand
{
    /// <summary>
    /// The alignment to apply to the Bottom document area.
    /// </summary>
    BottomAreaAlignment Alignment { get; set; }
}
