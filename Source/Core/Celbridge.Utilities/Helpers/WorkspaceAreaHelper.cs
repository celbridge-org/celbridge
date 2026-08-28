using Celbridge.Workspace;

namespace Celbridge.Utilities;

/// <summary>
/// Enumerates the workspace areas, describes which of them the user can hide, and resolves the document
/// area a workspace item opens in when the caller names no area.
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

    /// <summary>
    /// The document area a workspace item opens in when the caller names none: its default area when that
    /// is a document area, and otherwise the one document area it allows. Returns false when the item
    /// allows no document area, or allows several without defaulting to one of them.
    /// </summary>
    public static bool TryGetDocumentArea(
        IReadOnlyList<WorkspaceArea> allowedAreas,
        WorkspaceArea defaultArea,
        out WorkspaceArea documentArea)
    {
        if (IsDocumentArea(defaultArea)
            && allowedAreas.Contains(defaultArea))
        {
            documentArea = defaultArea;
            return true;
        }

        var documentAreas = new List<WorkspaceArea>();
        foreach (var area in allowedAreas)
        {
            if (IsDocumentArea(area))
            {
                documentAreas.Add(area);
            }
        }

        if (documentAreas.Count == 1)
        {
            documentArea = documentAreas[0];
            return true;
        }

        documentArea = WorkspaceArea.Utility;
        return false;
    }

    // Whether the area holds document tabs rather than the Utility Panel.
    private static bool IsDocumentArea(WorkspaceArea area)
    {
        return area != WorkspaceArea.Utility;
    }
}
