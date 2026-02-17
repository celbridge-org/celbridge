using Celbridge.Commands;
using Celbridge.UserInterface;

namespace Celbridge.Workspace;

/// <summary>
/// Resets a panel region to its default size.
/// </summary>
public interface IResetPanelCommand : IExecutableCommand
{
    /// <summary>
    /// The region to reset to default size.
    /// </summary>
    LayoutRegion Region { get; set; }
}
