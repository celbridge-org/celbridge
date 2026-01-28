using Celbridge.Commands;
using Celbridge.UserInterface;

namespace Celbridge.Workspace;

/// <summary>
/// Sets the visibility of workspace panels.
/// </summary>
public interface ISetPanelVisibilityCommand : IExecutableCommand
{
    /// <summary>
    /// Panel bitmask indicating which panels to show/hide.
    /// </summary>
    PanelVisibilityFlags Panels { get; set; }

    /// <summary>
    /// Whether to show or hide the specified panels.
    /// </summary>
    bool IsVisible { get; set; }
}
