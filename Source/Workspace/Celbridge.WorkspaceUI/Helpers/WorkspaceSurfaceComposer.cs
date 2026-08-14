using Celbridge.UserInterface.Helpers;
using Windows.Foundation;

namespace Celbridge.WorkspaceUI.Helpers;

/// <summary>
/// The set of workspace surfaces currently presented and how far the Bottom area spans across its
/// neighbours. A surface the Bottom area spans across stops above it instead of running full height.
/// </summary>
public record WorkspaceSurfacePresentation(
    bool IsMainAreaPresented,
    bool IsBottomAreaPresented,
    bool IsSideAreaPresented,
    bool IsUtilityPanelPresented,
    bool BottomAreaSpansUtilityPanel,
    bool BottomAreaSpansSideArea);

/// <summary>
/// The sizes the workspace composition reads off the live layout: the minimum each surface reports, the
/// channel between two surfaces, the space the workspace has to divide, and the widths the resizable
/// columns are holding. A column passes null while it is star sized, because a surface filling the
/// workspace is holding nothing back from its peer.
/// </summary>
public record WorkspaceSurfaceMetrics(
    Size MainAreaMinimumSize,
    Size BottomAreaMinimumSize,
    Size SideAreaMinimumSize,
    double UtilityPanelMinimumWidth,
    double GutterSize,
    Size WorkspaceExtent,
    double? UtilityPanelWidth,
    double? SideAreaWidth);

/// <summary>
/// Composes the floor every workspace surface is held at, and the largest each resizable surface can be
/// laid out at, from the minimums those surfaces report and the space the workspace has to divide. Holds no
/// layout of its own: the surface container reads the live sizes into the metrics, asks for a value, and
/// writes the answer onto its grid.
/// </summary>
public sealed class WorkspaceSurfaceComposer
{
    private readonly WorkspaceSurfacePresentation _presentation;
    private readonly WorkspaceSurfaceMetrics _metrics;
    private readonly Size _mainAreaMinimumSize;
    private readonly Size _bottomAreaMinimumSize;
    private readonly Size _sideAreaMinimumSize;
    private readonly double _utilityPanelMinimumWidth;

    public WorkspaceSurfaceComposer(WorkspaceSurfacePresentation presentation, WorkspaceSurfaceMetrics metrics)
    {
        _presentation = presentation;
        _metrics = metrics;

        // A surface that is not presented composes to zero, and takes the channel beside it with it, so the
        // zeroed track sizes that hand its space to its neighbours are not fighting a floor.
        _mainAreaMinimumSize = ResolvePresentedMinimum(metrics.MainAreaMinimumSize, presentation.IsMainAreaPresented);
        _bottomAreaMinimumSize = ResolvePresentedMinimum(metrics.BottomAreaMinimumSize, presentation.IsBottomAreaPresented);
        _sideAreaMinimumSize = ResolvePresentedMinimum(metrics.SideAreaMinimumSize, presentation.IsSideAreaPresented);

        _utilityPanelMinimumWidth = 0;
        if (presentation.IsUtilityPanelPresented)
        {
            _utilityPanelMinimumWidth = metrics.UtilityPanelMinimumWidth;
        }
    }

    /// <summary>
    /// The smallest size the workspace can be laid out at: every surface it is presenting at its own
    /// minimum, with the channels between them, and the channel above the document areas.
    /// </summary>
    public Size MinimumSize
    {
        get
        {
            double surfacesBesideUtilityPanel = WorkspaceMinimumSize.ComposeAdjacent(
                _utilityPanelMinimumWidth,
                DocumentAreasMinimumWidth,
                _metrics.GutterSize);

            double width = Math.Max(surfacesBesideUtilityPanel, BottomRowMinimumWidth);

            return new Size(width, DocumentAreasMinimumHeight + _metrics.GutterSize);
        }
    }

    /// <summary>
    /// The floor the Utility Panel's column holds, or zero while the panel is not presented.
    /// </summary>
    public double UtilityPanelMinimumWidth => _utilityPanelMinimumWidth;

    /// <summary>
    /// The floor the Bottom area's row holds, or zero while the area is not presented.
    /// </summary>
    public double BottomAreaMinimumHeight => _bottomAreaMinimumSize.Height;

    /// <summary>
    /// The floor the Side area's column holds, or zero while the area is not presented.
    /// </summary>
    public double SideAreaMinimumWidth => _sideAreaMinimumSize.Width;

    /// <summary>
    /// The floor the column the Main and Bottom areas share holds, which is the wider of the two. A Bottom
    /// area whose alignment spans a neighbour is not confined to the column and stops setting its floor.
    /// </summary>
    public double MainColumnMinimumWidth
    {
        get
        {
            if (_presentation.BottomAreaSpansUtilityPanel ||
                _presentation.BottomAreaSpansSideArea)
            {
                return _mainAreaMinimumSize.Width;
            }

            return Math.Max(_mainAreaMinimumSize.Width, _bottomAreaMinimumSize.Width);
        }
    }

    /// <summary>
    /// The floor the row the Main area sits in holds. A Bottom area whose alignment spans the Side area
    /// stops it above itself, which moves the Side area into this row alongside the Main area.
    /// </summary>
    public double MainRowMinimumHeight
    {
        get
        {
            if (!_presentation.BottomAreaSpansSideArea)
            {
                return _mainAreaMinimumSize.Height;
            }

            return Math.Max(_mainAreaMinimumSize.Height, _sideAreaMinimumSize.Height);
        }
    }

    /// <summary>
    /// The largest the Utility Panel can be laid out at without pushing a peer below its floor.
    /// </summary>
    public double AvailableUtilityPanelWidth => ComposeAvailableWidth(_utilityPanelMinimumWidth, SideAreaExcessWidth);

    /// <summary>
    /// The largest the Bottom area can be laid out at without pushing a peer below its floor.
    /// </summary>
    public double AvailableBottomAreaHeight => WorkspaceMinimumSize.SpaceForSurface(
        _metrics.WorkspaceExtent.Height,
        MinimumSize.Height,
        BottomAreaMinimumHeight);

    /// <summary>
    /// The largest the Side area can be laid out at without pushing a peer below its floor.
    /// </summary>
    public double AvailableSideAreaWidth => ComposeAvailableWidth(SideAreaMinimumWidth, UtilityPanelExcessWidth);

    /// <summary>
    /// Holds a Utility Panel width between the panel's own floor and the space the arrangement leaves it.
    /// </summary>
    public double ClampUtilityPanelWidth(double width)
    {
        return ClampWidth(width, _utilityPanelMinimumWidth, AvailableUtilityPanelWidth);
    }

    /// <summary>
    /// Holds a Bottom area height between the area's own floor and the space the arrangement leaves it.
    /// </summary>
    public double ClampBottomAreaHeight(double height)
    {
        double clampedHeight = Math.Max(height, BottomAreaMinimumHeight);

        // The workspace has no extent to divide until it has been laid out, which is where the stored sizes
        // arrive, so only the floor applies until then.
        if (_metrics.WorkspaceExtent.Height <= 0)
        {
            return clampedHeight;
        }

        return Math.Min(clampedHeight, AvailableBottomAreaHeight);
    }

    /// <summary>
    /// Holds a Side area width between the area's own floor and the space the arrangement leaves it.
    /// </summary>
    public double ClampSideAreaWidth(double width)
    {
        return ClampWidth(width, SideAreaMinimumWidth, AvailableSideAreaWidth);
    }

    // The width the document areas need beside the Utility Panel: the main column with the Side area, and
    // whatever of the Bottom area's row is not the Utility Panel's column.
    private double DocumentAreasMinimumWidth
    {
        get
        {
            double gutterSize = _metrics.GutterSize;
            double sideMinimumWidth = _sideAreaMinimumSize.Width;

            double mainRowWidth = WorkspaceMinimumSize.ComposeAdjacent(MainColumnMinimumWidth, sideMinimumWidth, gutterSize);

            double bottomRowWidth = _bottomAreaMinimumSize.Width;
            if (_presentation.BottomAreaSpansUtilityPanel)
            {
                // The Bottom area holds the Utility Panel's column too, so nothing of it sits beside the
                // panel and its floor is composed against the whole workspace instead.
                bottomRowWidth = 0;
            }
            else if (!_presentation.BottomAreaSpansSideArea)
            {
                bottomRowWidth = WorkspaceMinimumSize.ComposeAdjacent(bottomRowWidth, sideMinimumWidth, gutterSize);
            }

            return Math.Max(mainRowWidth, bottomRowWidth);
        }
    }

    // The width of the row the Bottom area sits in: its own extent, plus the surfaces its alignment has not
    // spanned across, which stay beside it in that row.
    private double BottomRowMinimumWidth
    {
        get
        {
            double gutterSize = _metrics.GutterSize;
            double width = _bottomAreaMinimumSize.Width;

            if (!_presentation.BottomAreaSpansUtilityPanel)
            {
                width = WorkspaceMinimumSize.ComposeAdjacent(_utilityPanelMinimumWidth, width, gutterSize);
            }

            if (!_presentation.BottomAreaSpansSideArea)
            {
                width = WorkspaceMinimumSize.ComposeAdjacent(width, _sideAreaMinimumSize.Width, gutterSize);
            }

            return width;
        }
    }

    // The height the document areas need: the Main area above the Bottom one. The Side area runs the full
    // height beside that pair, unless the Bottom area spans across it, which stops it above the Bottom area.
    private double DocumentAreasMinimumHeight
    {
        get
        {
            double height = WorkspaceMinimumSize.ComposeAdjacent(
                MainRowMinimumHeight,
                _bottomAreaMinimumSize.Height,
                _metrics.GutterSize);

            if (_presentation.BottomAreaSpansSideArea)
            {
                return height;
            }

            return Math.Max(height, _sideAreaMinimumSize.Height);
        }
    }

    // What the other resizable surface across the workspace is holding above its own floor. A surface sized
    // against its peers' floors alone would be offered space a peer is using and will not give up, which the
    // Main area absorbs until it reaches its own floor and the layout clips. The Bottom area is the only
    // resizable surface down the workspace, so nothing is held from it.
    private double UtilityPanelExcessWidth => ResolveSurfaceExcess(_metrics.UtilityPanelWidth, _utilityPanelMinimumWidth);

    private double SideAreaExcessWidth => ResolveSurfaceExcess(_metrics.SideAreaWidth, SideAreaMinimumWidth);

    // The largest a surface can be laid out at: its own floor, plus what the workspace has beyond the minimum
    // every presented surface needs, less what its peer is holding above its own floor.
    private double ComposeAvailableWidth(double surfaceMinimumWidth, double peerExcessWidth)
    {
        double workspaceMinimumWidth = MinimumSize.Width + peerExcessWidth;

        return WorkspaceMinimumSize.SpaceForSurface(_metrics.WorkspaceExtent.Width, workspaceMinimumWidth, surfaceMinimumWidth);
    }

    private double ClampWidth(double width, double surfaceMinimumWidth, double availableWidth)
    {
        double clampedWidth = Math.Max(width, surfaceMinimumWidth);

        // The workspace has no extent to divide until it has been laid out, which is where the stored sizes
        // arrive, so only the floor applies until then.
        if (_metrics.WorkspaceExtent.Width <= 0)
        {
            return clampedWidth;
        }

        return Math.Min(clampedWidth, availableWidth);
    }

    private static Size ResolvePresentedMinimum(Size minimumSize, bool isPresented)
    {
        if (!isPresented)
        {
            return new Size(0, 0);
        }

        return minimumSize;
    }

    private static double ResolveSurfaceExcess(double? surfaceWidth, double surfaceMinimumWidth)
    {
        if (surfaceWidth is not double width)
        {
            return 0;
        }

        return Math.Max(0, width - surfaceMinimumWidth);
    }
}
