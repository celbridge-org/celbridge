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

    /// <summary>
    /// The extent a surface can take beside a peer that is holding its minimum: everything the container has,
    /// less that minimum and the gutter between the two.
    /// </summary>
    public static double SpaceBeside(double containerExtent, double peerMinimum, double gutterSize)
    {
        if (peerMinimum <= 0)
        {
            return containerExtent;
        }

        return containerExtent - peerMinimum - gutterSize;
    }
}
