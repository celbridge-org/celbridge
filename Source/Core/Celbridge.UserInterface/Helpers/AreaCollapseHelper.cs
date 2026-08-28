using Celbridge.Workspace;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Maps a workspace area to the chevron that collapses it.
/// </summary>
public static class AreaCollapseHelper
{
    /// <summary>
    /// The chevron shown in the area's header, pointing towards the edge the area collapses into. Falls back
    /// to Close for an area that does not collapse to an edge.
    /// </summary>
    public static IconSymbol GetCollapseSymbol(this WorkspaceArea area)
    {
        switch (area)
        {
            case WorkspaceArea.Utility:
                return IconSymbol.ChevronLeft;

            case WorkspaceArea.Bottom:
                return IconSymbol.ChevronDown;

            case WorkspaceArea.Side:
                return IconSymbol.ChevronRight;

            default:
                return IconSymbol.Close;
        }
    }
}
