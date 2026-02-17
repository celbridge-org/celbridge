using Celbridge.Commands;
using Celbridge.UserInterface;

namespace Celbridge.Workspace;

/// <summary>
/// Resets a panel to its default size.
/// </summary>
public interface IResetPanelCommand : IExecutableCommand
{
    /// <summary>
    /// The panel to reset to default size.
    /// </summary>
    PanelVisibilityFlags Panel { get; set; }
}
