using Celbridge.Commands;
using Celbridge.UserInterface;

namespace Celbridge.Workspace;

/// <summary>
/// Sets the visibility of workspace panel regions.
/// </summary>
public interface ISetPanelVisibilityCommand : IExecutableCommand
{
    /// <summary>
    /// Region bitmask indicating which regions to show/hide.
    /// </summary>
    PanelRegion Regions { get; set; }

    /// <summary>
    /// Whether to show or hide the specified regions.
    /// </summary>
    bool IsVisible { get; set; }
}
