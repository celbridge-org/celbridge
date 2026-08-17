using Celbridge.Workspace;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Maps a workspace surface to the chevron that collapses it.
/// </summary>
public static class SurfaceCollapseHelper
{
    /// <summary>
    /// The chevron shown in the surface's header, pointing towards the edge the surface collapses into.
    /// Falls back to Close for a surface that does not collapse to an edge.
    /// </summary>
    public static IconSymbol GetCollapseSymbol(this WorkspaceSurface surface)
    {
        switch (surface)
        {
            case WorkspaceSurface.UtilityPanel:
                return IconSymbol.ChevronLeft;

            case WorkspaceSurface.BottomArea:
                return IconSymbol.ChevronDown;

            case WorkspaceSurface.SideArea:
                return IconSymbol.ChevronRight;

            default:
                return IconSymbol.Close;
        }
    }
}
