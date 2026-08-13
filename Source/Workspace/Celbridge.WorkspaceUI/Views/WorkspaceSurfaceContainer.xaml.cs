using Celbridge.Documents;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Views.Controls;
using Windows.Foundation;

namespace Celbridge.WorkspaceUI.Views;

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
/// Lays out the workspace surfaces: the Utility Panel it hosts, the three document-area grids, and the
/// splitters that size them. This control owns surface geometry only; the presentation is pushed in as a
/// snapshot, and the sections inside the area grids are managed by the documents panel.
/// </summary>
public sealed partial class WorkspaceSurfaceContainer : UserControl
{
    // Positions in the workspace grid that the Bottom area's alignment moves things between. The Utility
    // Panel starts a row above the document areas because it runs up into the title bar, so the surfaces
    // that stop above the Bottom area each have their own full and shortened span.
    private const int UtilityPanelColumnIndex = 0;
    private const int MainAreaColumnIndex = 2;
    private const int UtilityPanelHostRowSpan = 4;
    private const int UtilityPanelSplitterRowSpan = 3;
    private const int SideAreaRowSpan = 3;

    private readonly UtilityPanel _utilityPanel;

    private SplitterHelper? _utilityPanelSplitterHelper;
    private SplitterHelper? _bottomAreaSplitterHelper;
    private SplitterHelper? _sideAreaSplitterHelper;

    private WorkspaceSurfacePresentation _presentation = new(
        IsMainAreaPresented: true,
        IsBottomAreaPresented: true,
        IsSideAreaPresented: true,
        IsUtilityPanelPresented: true,
        BottomAreaSpansUtilityPanel: false,
        BottomAreaSpansSideArea: false);

    /// <summary>
    /// Raised when a resizable surface is dragged to a new size, carrying its new height (Bottom area)
    /// or width (Utility Panel, Side area).
    /// </summary>
    public event Action<WorkspaceSurface, double>? SurfaceSizeChanged;

    /// <summary>
    /// Raised when a surface splitter is double-clicked, asking for that surface's default size.
    /// </summary>
    public event Action<WorkspaceSurface>? SurfaceSizeResetRequested;

    /// <summary>
    /// Snap targets for the Bottom area splitter, supplied by the documents panel because they derive
    /// from the sections inside the area grids. Null means no snapping.
    /// </summary>
    public Func<IReadOnlyList<double>>? BottomAreaSplitterSnapTargets { get; set; }

    /// <summary>
    /// The minimum size of a document area, supplied by the documents panel because it derives from the
    /// sections inside that area's grid. Zero for an area that is not presented, and while unset.
    /// </summary>
    public Func<DocumentArea, Size>? AreaMinimumSizes { get; set; }

    /// <summary>
    /// The Utility Panel hosted in the workspace layout.
    /// </summary>
    public IUtilityPanel UtilityPanel => _utilityPanel;

    public WorkspaceSurfaceContainer()
    {
        InitializeComponent();

        DocumentAreaGutterRow.Height = new GridLength(GutterSize);

        // The stored sizes arrive with the workspace settings, so the resizable surfaces open at their
        // defaults until then.
        UtilityPanelColumn.Width = new GridLength(WorkspaceConstants.UtilityPanelWidth);
        SideAreaColumn.Width = new GridLength(WorkspaceConstants.SideAreaWidth);
        BottomAreaRow.Height = new GridLength(WorkspaceConstants.BottomAreaHeight);

        // The Utility Panel is part of the workspace layout rather than a sibling of it, so this
        // container creates and hosts it.
        var utilityPanel = ServiceLocator.AcquireService<IUtilityPanel>();
        _utilityPanel = (UtilityPanel)utilityPanel;
        UtilityPanelHost.Children.Add(_utilityPanel);

        InitializeSurfaceSplitters();
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
                UtilityPanelMinimumWidth,
                DocumentAreasMinimumWidth,
                GutterSize);

            double width = Math.Max(surfacesBesideUtilityPanel, BottomRowMinimumWidth);

            return new Size(width, DocumentAreasMinimumHeight + GutterSize);
        }
    }

    // The width the document areas need beside the Utility Panel: the main column with the Side area, and
    // whatever of the Bottom area's row is not the Utility Panel's column.
    private double DocumentAreasMinimumWidth
    {
        get
        {
            double gutterSize = GutterSize;
            double sideMinimumWidth = GetAreaMinimumSize(DocumentArea.Side).Width;

            double mainRowWidth = WorkspaceMinimumSize.ComposeAdjacent(MainColumnMinimumWidth, sideMinimumWidth, gutterSize);

            double bottomRowWidth = GetAreaMinimumSize(DocumentArea.Bottom).Width;
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
            double gutterSize = GutterSize;
            double width = GetAreaMinimumSize(DocumentArea.Bottom).Width;

            if (!_presentation.BottomAreaSpansUtilityPanel)
            {
                width = WorkspaceMinimumSize.ComposeAdjacent(UtilityPanelMinimumWidth, width, gutterSize);
            }

            if (!_presentation.BottomAreaSpansSideArea)
            {
                width = WorkspaceMinimumSize.ComposeAdjacent(width, GetAreaMinimumSize(DocumentArea.Side).Width, gutterSize);
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
            double gutterSize = GutterSize;
            double sideMinimumHeight = GetAreaMinimumSize(DocumentArea.Side).Height;

            double mainRowHeight = GetAreaMinimumSize(DocumentArea.Main).Height;
            if (_presentation.BottomAreaSpansSideArea)
            {
                mainRowHeight = Math.Max(mainRowHeight, sideMinimumHeight);
            }

            double height = WorkspaceMinimumSize.ComposeAdjacent(
                mainRowHeight,
                GetAreaMinimumSize(DocumentArea.Bottom).Height,
                gutterSize);

            if (_presentation.BottomAreaSpansSideArea)
            {
                return height;
            }

            return Math.Max(height, sideMinimumHeight);
        }
    }

    // The Main and Bottom areas share a column, so the column holds the wider of the two. A Bottom area whose
    // alignment spans a neighbour is not confined to the column and stops setting its floor.
    private double MainColumnMinimumWidth
    {
        get
        {
            double mainMinimumWidth = GetAreaMinimumSize(DocumentArea.Main).Width;

            if (_presentation.BottomAreaSpansUtilityPanel ||
                _presentation.BottomAreaSpansSideArea)
            {
                return mainMinimumWidth;
            }

            return Math.Max(mainMinimumWidth, GetAreaMinimumSize(DocumentArea.Bottom).Width);
        }
    }

    // Zero while the Utility Panel is not presented, so it contributes nothing to the workspace minimum and
    // its channel goes with it.
    private double UtilityPanelMinimumWidth
    {
        get
        {
            if (!_presentation.IsUtilityPanelPresented)
            {
                return 0;
            }

            return _utilityPanel.MinimumWidth;
        }
    }

    private Size GetAreaMinimumSize(DocumentArea area)
    {
        if (AreaMinimumSizes is null)
        {
            return new Size(0, 0);
        }

        return AreaMinimumSizes(area);
    }

    // The channel between two surfaces. The splitter in it takes this size, which is what holds the gap open.
    private static double GutterSize => (double)Application.Current.Resources["GutterSize"];

    /// <summary>
    /// Gets the grid a document area's sections are laid out in.
    /// </summary>
    public Grid GetAreaGrid(DocumentArea area)
    {
        switch (area)
        {
            case DocumentArea.Main:
                return MainAreaGrid;

            case DocumentArea.Bottom:
                return BottomAreaGrid;

            default:
                return SideAreaGrid;
        }
    }

    /// <summary>
    /// Applies a presentation snapshot: which surfaces are visible, and which neighbours the Bottom
    /// area spans across. Main shares its column with Bottom, so an area only takes a fixed size while
    /// it sits alongside another one; the sole presented area takes the whole panel.
    /// </summary>
    public void ApplyPresentation(WorkspaceSurfacePresentation presentation)
    {
        _presentation = presentation;

        bool isMainPresented = presentation.IsMainAreaPresented;
        bool isBottomPresented = presentation.IsBottomAreaPresented;
        bool isSidePresented = presentation.IsSideAreaPresented;
        bool isUtilityPanelPresented = presentation.IsUtilityPanelPresented;
        bool isMainColumnPresented = isMainPresented || isBottomPresented;

        ApplyBottomAreaSpans(presentation);

        MainAreaGrid.Visibility = isMainPresented ? Visibility.Visible : Visibility.Collapsed;
        BottomAreaGrid.Visibility = isBottomPresented ? Visibility.Visible : Visibility.Collapsed;
        SideAreaGrid.Visibility = isSidePresented ? Visibility.Visible : Visibility.Collapsed;
        UtilityPanelHost.Visibility = isUtilityPanelPresented ? Visibility.Visible : Visibility.Collapsed;

        // A splitter only earns its place between two presented surfaces.
        bool showBottomSplitter = isMainPresented && isBottomPresented;
        bool showSideSplitter = isSidePresented && isMainColumnPresented;
        BottomAreaSplitter.Visibility = showBottomSplitter ? Visibility.Visible : Visibility.Collapsed;
        SideAreaSplitter.Visibility = showSideSplitter ? Visibility.Visible : Visibility.Collapsed;
        UtilityPanelSplitter.Visibility = isUtilityPanelPresented ? Visibility.Visible : Visibility.Collapsed;

        UtilityPanelColumn.MinWidth = UtilityPanelMinimumWidth;
        UtilityPanelHost.MinWidth = UtilityPanelMinimumWidth;
        if (!isUtilityPanelPresented)
        {
            UtilityPanelColumn.Width = new GridLength(0);
        }

        // Main's row is only zeroed to hand its column over to the Bottom area. The Side area can span
        // all three document rows, so zeroing them when it is the only presented area would leave it no
        // height. Main's own minimums stay at zero: they are enforced while dragging, by the splitter
        // helpers.
        bool mainRowTakesRemainingHeight = isMainPresented || !isBottomPresented;
        MainAreaRow.Height = mainRowTakesRemainingHeight
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);

        if (!isBottomPresented)
        {
            BottomAreaRow.Height = new GridLength(0);
        }
        else if (!isMainPresented)
        {
            BottomAreaRow.Height = new GridLength(1, GridUnitType.Star);
        }

        double bottomMinimumHeight = 0;
        if (showBottomSplitter)
        {
            bottomMinimumHeight = GetAreaMinimumSize(DocumentArea.Bottom).Height;
        }
        BottomAreaRow.MinHeight = bottomMinimumHeight;

        MainAreaColumn.Width = isMainColumnPresented ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        if (!isSidePresented)
        {
            SideAreaColumn.Width = new GridLength(0);
        }
        else if (!isMainColumnPresented)
        {
            SideAreaColumn.Width = new GridLength(1, GridUnitType.Star);
        }

        double sideMinimumWidth = 0;
        if (showSideSplitter)
        {
            sideMinimumWidth = GetAreaMinimumSize(DocumentArea.Side).Width;
        }
        SideAreaColumn.MinWidth = sideMinimumWidth;
    }

    /// <summary>
    /// Sets the width of the Utility Panel, the height of the Bottom area or the width of the Side area.
    /// Ignored while the surface is hidden, and for an area that is the only one presented, because a
    /// sole presented area fills the panel.
    /// </summary>
    public void SetSurfaceSize(WorkspaceSurface surface, double size)
    {
        if (size <= 0)
        {
            return;
        }

        bool isMainColumnPresented = _presentation.IsMainAreaPresented || _presentation.IsBottomAreaPresented;

        switch (surface)
        {
            case WorkspaceSurface.UtilityPanel:
                if (_presentation.IsUtilityPanelPresented)
                {
                    UtilityPanelColumn.Width = new GridLength(size);
                }
                break;

            case WorkspaceSurface.BottomArea:
                if (_presentation.IsBottomAreaPresented &&
                    _presentation.IsMainAreaPresented)
                {
                    BottomAreaRow.Height = new GridLength(size);
                }
                break;

            case WorkspaceSurface.SideArea:
                if (_presentation.IsSideAreaPresented &&
                    isMainColumnPresented)
                {
                    SideAreaColumn.Width = new GridLength(size);
                }
                break;
        }
    }

    // Spans the Bottom area, and the splitter that sizes it, across the surfaces the presentation says
    // it covers, and stops those surfaces above it. The columns of a hidden surface are already zero
    // wide, so the spans need no visibility conditions of their own.
    private void ApplyBottomAreaSpans(WorkspaceSurfacePresentation presentation)
    {
        bool spansUtilityPanel = presentation.BottomAreaSpansUtilityPanel;
        bool spansSideArea = presentation.BottomAreaSpansSideArea;

        int bottomColumn = spansUtilityPanel ? UtilityPanelColumnIndex : MainAreaColumnIndex;
        int bottomColumnSpan = 1;
        if (spansUtilityPanel)
        {
            bottomColumnSpan += 2;
        }
        if (spansSideArea)
        {
            bottomColumnSpan += 2;
        }

        Grid.SetColumn(BottomAreaGrid, bottomColumn);
        Grid.SetColumnSpan(BottomAreaGrid, bottomColumnSpan);
        Grid.SetColumn(BottomAreaSplitter, bottomColumn);
        Grid.SetColumnSpan(BottomAreaSplitter, bottomColumnSpan);

        Grid.SetRowSpan(UtilityPanelHost, spansUtilityPanel ? 2 : UtilityPanelHostRowSpan);
        Grid.SetRowSpan(UtilityPanelSplitter, spansUtilityPanel ? 1 : UtilityPanelSplitterRowSpan);

        Grid.SetRowSpan(SideAreaGrid, spansSideArea ? 1 : SideAreaRowSpan);
        Grid.SetRowSpan(SideAreaSplitter, spansSideArea ? 1 : SideAreaRowSpan);

        // The panel meets the application border until the Bottom area runs under it, which puts a
        // gutter below it to draw an edge against.
        _utilityPanel.SetBottomEdgePresented(spansUtilityPanel);
    }

    // The tracks the splitters resize are indexed into the one workspace grid. Bottom and Side are
    // measured from the far edge, so their deltas are inverted; the Utility Panel is measured from the
    // near edge.
    private void InitializeSurfaceSplitters()
    {
        // The Utility Panel takes whatever the document areas beside it do not need. The two area splitters
        // divide a pair of tracks whose extent excludes the gutter between them, so the space left for one is
        // the pair less the other's minimum.
        _utilityPanelSplitterHelper = new SplitterHelper(
            RootGrid,
            GridResizeMode.Columns,
            0,
            minSizeFunc: () => UtilityPanelMinimumWidth,
            maxSizeFunc: () => WorkspaceMinimumSize.SpaceBeside(
                RootGrid.ActualWidth,
                DocumentAreasMinimumWidth,
                GutterSize));

        _bottomAreaSplitterHelper = new SplitterHelper(
            RootGrid,
            GridResizeMode.Rows,
            3,
            minSizeFunc: () => GetAreaMinimumSize(DocumentArea.Bottom).Height,
            invertDelta: true,
            maxSizeFunc: () => MainAreaRow.ActualHeight + BottomAreaRow.ActualHeight - GetAreaMinimumSize(DocumentArea.Main).Height)
        {
            SnapTargets = () => BottomAreaSplitterSnapTargets?.Invoke() ?? Array.Empty<double>()
        };

        _sideAreaSplitterHelper = new SplitterHelper(
            RootGrid,
            GridResizeMode.Columns,
            4,
            minSizeFunc: () => GetAreaMinimumSize(DocumentArea.Side).Width,
            invertDelta: true,
            maxSizeFunc: () => MainAreaColumn.ActualWidth + SideAreaColumn.ActualWidth - MainColumnMinimumWidth);

        UtilityPanelSplitter.DragStarted += (s, e) => _utilityPanelSplitterHelper?.OnDragStarted();
        UtilityPanelSplitter.DragDelta += (s, delta) => _utilityPanelSplitterHelper?.OnDragDelta(delta);
        UtilityPanelSplitter.DragCompleted += (s, e) => SurfaceSizeChanged?.Invoke(WorkspaceSurface.UtilityPanel, UtilityPanelColumn.ActualWidth);
        UtilityPanelSplitter.DoubleClicked += (s, e) => SurfaceSizeResetRequested?.Invoke(WorkspaceSurface.UtilityPanel);

        BottomAreaSplitter.DragStarted += (s, e) => _bottomAreaSplitterHelper?.OnDragStarted();
        BottomAreaSplitter.DragDelta += (s, delta) => _bottomAreaSplitterHelper?.OnDragDelta(delta);
        BottomAreaSplitter.DragCompleted += (s, e) => SurfaceSizeChanged?.Invoke(WorkspaceSurface.BottomArea, BottomAreaRow.ActualHeight);
        BottomAreaSplitter.DoubleClicked += (s, e) => SurfaceSizeResetRequested?.Invoke(WorkspaceSurface.BottomArea);

        SideAreaSplitter.DragStarted += (s, e) => _sideAreaSplitterHelper?.OnDragStarted();
        SideAreaSplitter.DragDelta += (s, delta) => _sideAreaSplitterHelper?.OnDragDelta(delta);
        SideAreaSplitter.DragCompleted += (s, e) => SurfaceSizeChanged?.Invoke(WorkspaceSurface.SideArea, SideAreaColumn.ActualWidth);
        SideAreaSplitter.DoubleClicked += (s, e) => SurfaceSizeResetRequested?.Invoke(WorkspaceSurface.SideArea);
    }
}
