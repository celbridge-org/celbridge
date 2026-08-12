using Celbridge.Documents;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Views.Controls;

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
    private const double MinUtilityPanelWidth = 200;
    private const double MinDocumentAreasWidth = 200;
    private const double MinBottomAreaHeight = 150;
    private const double MinSideAreaWidth = 200;
    private const double MinMainAreaWidth = 200;
    private const double MinMainAreaHeight = 150;

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
    /// The Utility Panel hosted in the workspace layout.
    /// </summary>
    public IUtilityPanel UtilityPanel => _utilityPanel;

    public WorkspaceSurfaceContainer()
    {
        InitializeComponent();

        double gutterSize = (double)Application.Current.Resources["GutterSize"];
        DocumentAreaGutterRow.Height = new GridLength(gutterSize);

        // The Utility Panel is part of the workspace layout rather than a sibling of it, so this
        // container creates and hosts it.
        var utilityPanel = ServiceLocator.AcquireService<IUtilityPanel>();
        _utilityPanel = (UtilityPanel)utilityPanel;
        UtilityPanelHost.Children.Add(_utilityPanel);

        InitializeSurfaceSplitters();
    }

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

        UtilityPanelColumn.MinWidth = isUtilityPanelPresented ? MinUtilityPanelWidth : 0;
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

        BottomAreaRow.MinHeight = showBottomSplitter ? MinBottomAreaHeight : 0;

        MainAreaColumn.Width = isMainColumnPresented ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        if (!isSidePresented)
        {
            SideAreaColumn.Width = new GridLength(0);
        }
        else if (!isMainColumnPresented)
        {
            SideAreaColumn.Width = new GridLength(1, GridUnitType.Star);
        }

        SideAreaColumn.MinWidth = showSideSplitter ? MinSideAreaWidth : 0;
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
        _utilityPanelSplitterHelper = new SplitterHelper(
            RootGrid,
            GridResizeMode.Columns,
            0,
            minSize: MinUtilityPanelWidth,
            maxSizeFunc: () => RootGrid.ActualWidth - MinDocumentAreasWidth);

        _bottomAreaSplitterHelper = new SplitterHelper(
            RootGrid,
            GridResizeMode.Rows,
            3,
            minSize: MinBottomAreaHeight,
            invertDelta: true,
            maxSizeFunc: () => MainAreaRow.ActualHeight + BottomAreaRow.ActualHeight - MinMainAreaHeight)
        {
            SnapTargets = () => BottomAreaSplitterSnapTargets?.Invoke() ?? Array.Empty<double>()
        };

        _sideAreaSplitterHelper = new SplitterHelper(
            RootGrid,
            GridResizeMode.Columns,
            4,
            minSize: MinSideAreaWidth,
            invertDelta: true,
            maxSizeFunc: () => MainAreaColumn.ActualWidth + SideAreaColumn.ActualWidth - MinMainAreaWidth);

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
