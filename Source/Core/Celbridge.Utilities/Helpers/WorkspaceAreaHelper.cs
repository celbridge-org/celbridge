using Celbridge.Workspace;

namespace Celbridge.Utilities;

/// <summary>
/// Enumerates the workspace areas and describes which of them the user can hide.
/// </summary>
public static class WorkspaceAreaHelper
{
    /// <summary>
    /// Every area, in the order they read on screen.
    /// </summary>
    public static readonly IReadOnlyList<WorkspaceArea> AllAreas =
    [
        WorkspaceArea.Utility,
        WorkspaceArea.Main,
        WorkspaceArea.Bottom,
        WorkspaceArea.Side
    ];

    /// <summary>
    /// Every area the user can hide, in the order they read on screen. Main is absent because it always shows.
    /// </summary>
    public static readonly IReadOnlyList<WorkspaceArea> CollapsibleAreas =
    [
        WorkspaceArea.Utility,
        WorkspaceArea.Bottom,
        WorkspaceArea.Side
    ];

    /// <summary>
    /// Every area showing at once, which is the layout a workspace opens with.
    /// </summary>
    public static readonly IReadOnlySet<WorkspaceArea> AllAreasVisible = new HashSet<WorkspaceArea>(AllAreas);

    /// <summary>
    /// Whether the user can hide the area. False for Main, which always shows.
    /// </summary>
    public static bool IsCollapsible(this WorkspaceArea area)
    {
        return area != WorkspaceArea.Main;
    }
}
