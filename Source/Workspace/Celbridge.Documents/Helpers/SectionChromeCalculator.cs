using Celbridge.Documents.Services;

namespace Celbridge.Documents.Helpers;

/// <summary>
/// The border edges a section draws against the gutters around it, and the rounding of its corners.
/// </summary>
public record SectionChrome(Thickness Edges, CornerRadius Corners);

/// <summary>
/// The chrome for both sections of an area. Secondary is null while the area is unsplit.
/// </summary>
public record AreaSectionChrome(SectionChrome Primary, SectionChrome? Secondary);

/// <summary>
/// Computes each document section's gutter chrome from the area layout state. A section draws every edge
/// that faces a gutter and leaves bare the edges that meet the application border, which is its own
/// boundary. Only the edges on the outside of the section's area shape its corners, so the two sections
/// of a split area share one rounded perimeter with a square cut down the middle, which is what marks
/// them as belonging to the same area.
/// </summary>
public class SectionChromeCalculator
{
    /// <summary>
    /// The thickness of one edge a section draws against a gutter.
    /// </summary>
    public const double EdgeThickness = 1.0;

    private readonly AreaLayoutState _layoutState;
    private readonly bool _roundsBottomCorners;

    /// <summary>
    /// The bottom corners are filled by the document view rather than by the section's own chrome, so
    /// roundsBottomCorners is true only on a head that clips a hosted web view to the rounding.
    /// </summary>
    public SectionChromeCalculator(AreaLayoutState layoutState, bool roundsBottomCorners)
    {
        _layoutState = layoutState;
        _roundsBottomCorners = roundsBottomCorners;
    }

    /// <summary>
    /// Computes the chrome for an area's sections. An unsplit area has one section that takes all of the
    /// area's edges; a split one gives each section the outer edges on its own side plus an inner edge
    /// facing the split gutter.
    /// </summary>
    public AreaSectionChrome CalculateAreaChrome(DocumentArea area, double cornerRadius)
    {
        var areaEdges = ResolveAreaEdges(area);

        if (!_layoutState.IsAreaSplit(area))
        {
            // Nothing is internal to an unsplit area, so every edge it draws also shapes its corners.
            var chrome = new SectionChrome(areaEdges, ResolveCorners(areaEdges, cornerRadius));

            return new AreaSectionChrome(chrome, null);
        }

        if (area.SplitsHorizontally())
        {
            var leftEdges = new Thickness(areaEdges.Left, areaEdges.Top, EdgeThickness, areaEdges.Bottom);
            var rightEdges = new Thickness(EdgeThickness, areaEdges.Top, areaEdges.Right, areaEdges.Bottom);

            var leftOuterEdges = new Thickness(areaEdges.Left, areaEdges.Top, 0, areaEdges.Bottom);
            var rightOuterEdges = new Thickness(0, areaEdges.Top, areaEdges.Right, areaEdges.Bottom);

            return new AreaSectionChrome(
                new SectionChrome(leftEdges, ResolveCorners(leftOuterEdges, cornerRadius)),
                new SectionChrome(rightEdges, ResolveCorners(rightOuterEdges, cornerRadius)));
        }

        var topEdges = new Thickness(areaEdges.Left, areaEdges.Top, areaEdges.Right, EdgeThickness);
        var bottomEdges = new Thickness(areaEdges.Left, EdgeThickness, areaEdges.Right, areaEdges.Bottom);

        var topOuterEdges = new Thickness(areaEdges.Left, areaEdges.Top, areaEdges.Right, 0);
        var bottomOuterEdges = new Thickness(areaEdges.Left, 0, areaEdges.Right, areaEdges.Bottom);

        return new AreaSectionChrome(
            new SectionChrome(topEdges, ResolveCorners(topOuterEdges, cornerRadius)),
            new SectionChrome(bottomEdges, ResolveCorners(bottomOuterEdges, cornerRadius)));
    }

    // An area draws the edges that face another panel and leaves bare the edges that meet the application
    // border. The top edge always faces the title bar gutter.
    private Thickness ResolveAreaEdges(DocumentArea area)
    {
        double facingUtilityPanel = ResolveEdge(_layoutState.IsUtilityPanelPresented);

        if (area == DocumentArea.Side)
        {
            // The Side area's left edge faces the main column, or the Utility Panel when no other area is
            // presented alongside it.
            bool isMainColumnPresented = _layoutState.IsAreaPresented(DocumentArea.Main) ||
                _layoutState.IsAreaPresented(DocumentArea.Bottom);
            double sideLeft = ResolveEdge(isMainColumnPresented || _layoutState.IsUtilityPanelPresented);

            return new Thickness(sideLeft, EdgeThickness, 0, 0);
        }

        double facingSide = ResolveEdge(_layoutState.IsAreaPresented(DocumentArea.Side));

        if (area == DocumentArea.Bottom)
        {
            return new Thickness(facingUtilityPanel, EdgeThickness, facingSide, 0);
        }

        double facingBottom = ResolveEdge(_layoutState.IsAreaPresented(DocumentArea.Bottom));

        return new Thickness(facingUtilityPanel, EdgeThickness, facingSide, facingBottom);
    }

    private static double ResolveEdge(bool facesNeighbour)
    {
        if (facesNeighbour)
        {
            return EdgeThickness;
        }

        return 0;
    }

    // A corner is rounded where both of the edges meeting there face a gutter outside the area, so a corner
    // sitting on the application border or on an area's internal split stays square.
    private CornerRadius ResolveCorners(Thickness outerEdges, double cornerRadius)
    {
        double bottomRadius = 0;
        if (_roundsBottomCorners)
        {
            bottomRadius = cornerRadius;
        }

        double topLeft = ResolveCorner(outerEdges.Left, outerEdges.Top, cornerRadius);
        double topRight = ResolveCorner(outerEdges.Top, outerEdges.Right, cornerRadius);
        double bottomRight = ResolveCorner(outerEdges.Right, outerEdges.Bottom, bottomRadius);
        double bottomLeft = ResolveCorner(outerEdges.Bottom, outerEdges.Left, bottomRadius);

        return new CornerRadius(topLeft, topRight, bottomRight, bottomLeft);
    }

    private static double ResolveCorner(double firstEdge, double secondEdge, double radius)
    {
        if (firstEdge > 0 &&
            secondEdge > 0)
        {
            return radius;
        }

        return 0;
    }
}
