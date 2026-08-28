using Celbridge.Workspace;
using Windows.Foundation;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Composes the workspace minimum sizes from one floor: the smallest size at which a document is legible.
/// A container asks the surfaces it is presenting for their minimums and composes its own from them, so a
/// minimum that changes because a surface was revealed or an area was split is right everywhere at once
/// without any clamp site naming a value.
/// </summary>
public static class WorkspaceMinimumSize
{
    /// <summary>
    /// The smallest size a surface hosting a document can take: the document floor plus the chrome the
    /// surface draws around it. A document section and the Utility Panel's content area both compose this
    /// way.
    /// </summary>
    public static Size ComposeSection(Size surfaceChrome)
    {
        return new Size(
            WorkspaceConstants.DocumentMinWidth + surfaceChrome.Width,
            WorkspaceConstants.DocumentMinHeight + surfaceChrome.Height);
    }

    /// <summary>
    /// The smallest width the Utility Panel can be laid out at: the document floor plus the edges its content
    /// area draws down each side, which it carves out like a document section.
    /// </summary>
    public static double ComposeUtilityPanelWidth()
    {
        double edges = WorkspaceConstants.SectionEdgeThickness * 2;
        var panelChrome = new Size(edges, edges);

        return ComposeSection(panelChrome).Width;
    }

    /// <summary>
    /// The smallest size the workspace can be laid out at in the layout a workspace opens with: the Utility
    /// Rail, plus the given visible areas, each unsplit and holding the floor composed from the authored
    /// chrome, with the channels between them and the one above the document areas. Nothing here is measured,
    /// so this holds before any workspace exists.
    /// </summary>
    public static Size ComposeDefaultLayout(IReadOnlySet<WorkspaceArea> visibleAreas, double gutterSize)
    {
        double edges = WorkspaceConstants.SectionEdgeThickness * 2;
        var sectionChrome = new Size(edges, WorkspaceConstants.SectionTabStripHeight + edges);
        var sectionMinimum = ComposeSection(sectionChrome);

        // No area is split in the default layout, so every document area takes one section, and the Main area
        // is always shown.
        double sideMinimumWidth = 0;
        if (visibleAreas.Contains(WorkspaceArea.Side))
        {
            sideMinimumWidth = sectionMinimum.Width;
        }

        double bottomMinimumHeight = 0;
        if (visibleAreas.Contains(WorkspaceArea.Bottom))
        {
            bottomMinimumHeight = sectionMinimum.Height;
        }

        double utilityPanelMinimumWidth = 0;
        if (visibleAreas.Contains(WorkspaceArea.Utility))
        {
            utilityPanelMinimumWidth = ComposeUtilityPanelWidth();
        }

        double documentAreasWidth = ComposeAdjacent(sectionMinimum.Width, sideMinimumWidth, gutterSize);
        double width = ComposeAdjacent(utilityPanelMinimumWidth, documentAreasWidth, gutterSize);

        // The rail is always on screen and meets whatever sits beside it directly, so its width is added
        // without a channel.
        width += WorkspaceConstants.UtilityRailWidth;

        double documentAreasHeight = ComposeAdjacent(sectionMinimum.Height, bottomMinimumHeight, gutterSize);

        return new Size(width, documentAreasHeight);
    }

    /// <summary>
    /// The smallest size a document area can take. A split area holds two sections along its split axis,
    /// with a gutter between them.
    /// </summary>
    public static Size ComposeArea(Size sectionMinimum, bool isSplit, bool splitsHorizontally, double gutterSize)
    {
        if (!isSplit)
        {
            return sectionMinimum;
        }

        if (splitsHorizontally)
        {
            double splitWidth = ComposeAdjacent(sectionMinimum.Width, sectionMinimum.Width, gutterSize);

            return new Size(splitWidth, sectionMinimum.Height);
        }

        double splitHeight = ComposeAdjacent(sectionMinimum.Height, sectionMinimum.Height, gutterSize);

        return new Size(sectionMinimum.Width, splitHeight);
    }

    /// <summary>
    /// The extent two surfaces side by side need, including the gutter between them. A surface that is not
    /// presented passes zero and takes its gutter with it.
    /// </summary>
    public static double ComposeAdjacent(double firstMinimum, double secondMinimum, double gutterSize)
    {
        if (firstMinimum <= 0)
        {
            return secondMinimum;
        }

        if (secondMinimum <= 0)
        {
            return firstMinimum;
        }

        return firstMinimum + gutterSize + secondMinimum;
    }

    /// <summary>
    /// The largest a surface can be laid out at without pushing a peer below its floor: its own minimum, plus
    /// whatever the container has beyond the minimum it needs for everything it is presenting. Returns the
    /// surface's own minimum once the container is below that minimum, which is the point where space runs out
    /// for every surface at once and the excess is clipped instead.
    /// </summary>
    public static double SpaceForSurface(double containerExtent, double containerMinimum, double surfaceMinimum)
    {
        double slack = containerExtent - containerMinimum;

        if (slack <= 0)
        {
            return surfaceMinimum;
        }

        return surfaceMinimum + slack;
    }

}
