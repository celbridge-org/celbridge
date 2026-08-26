using Celbridge.Documents;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Views.Controls;
using Celbridge.WorkspaceUI.Helpers;
using Windows.Foundation;

namespace Celbridge.WorkspaceUI.Views;

/// <summary>
/// Lays out the workspace surfaces: the Utility Rail and Utility Panel it hosts, the three document-area
/// grids, and the splitters that size them. This control owns surface geometry only, with the presentation
/// pushed in as a snapshot.
/// </summary>
public sealed partial class WorkspaceSurfaceContainer : UserControl
{
    // Positions in the workspace grid: the tracks each surface sits in. A surface beside the document areas
    // runs the full height of the grid, and shortens to the Main area row alone once the Bottom area runs
    // underneath it.
    private const int UtilityPanelColumnIndex = 1;
    private const int MainAreaColumnIndex = 3;
    private const int SideAreaColumnIndex = 5;
    private const int BottomAreaRowIndex = 2;
    private const int FullHeightRowSpan = 3;
    private const int AboveBottomAreaRowSpan = 1;

    private readonly UtilityPanel _utilityPanel;

    private SplitterHelper? _utilityPanelSplitterHelper;
    private SplitterHelper? _bottomAreaSplitterHelper;
    private SplitterHelper? _sideAreaSplitterHelper;

    private WorkspaceSurfacePresentation _presentation = new(
        IsMainAreaPresented: true,
        IsBottomAreaPresented: true,
        IsSideAreaPresented: true,
        IsUtilityPanelPresented: true,
        IsUtilityRailPresented: true,
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
    /// Raised when the space the workspace has to divide changes, asking for the stored surface sizes to be
    /// applied again. The stored size is the one the user set, so a surface held narrower to fit a smaller
    /// window comes back to it when the space returns.
    /// </summary>
    public event Action? StoredSurfaceSizesNeeded;

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

        // The stored sizes arrive with the workspace settings, so the resizable surfaces open at their
        // defaults until then.
        UtilityPanelColumn.Width = new GridLength(WorkspaceConstants.UtilityPanelWidth);
        SideAreaColumn.Width = new GridLength(WorkspaceConstants.SideAreaWidth);
        BottomAreaRow.Height = new GridLength(WorkspaceConstants.BottomAreaHeight);

        // This container creates and hosts the Utility Panel. Its rail is hosted in a column of its own,
        // which keeps the rail on screen while the panel is collapsed.
        var utilityPanel = ServiceLocator.AcquireService<IUtilityPanel>();
        _utilityPanel = (UtilityPanel)utilityPanel;
        UtilityPanelHost.Children.Add(_utilityPanel);
        UtilityRailHost.Children.Add(_utilityPanel.Rail);

        InitializeSurfaceSplitters();

        // A pixel-sized surface holds its width as the window shrinks, so the star tracks take the whole
        // shortfall and the surfaces past them are pushed off the window once those reach their floors. The
        // sizes are applied again against the new extent instead, which holds each surface to what still fits.
        SizeChanged += (s, e) => StoredSurfaceSizesNeeded?.Invoke();
    }

    /// <summary>
    /// The smallest size the workspace can be laid out at: every surface it is presenting at its own
    /// minimum, with the channels between them, and the channel above the document areas.
    /// </summary>
    public Size MinimumSize => CreateComposer().MinimumSize;

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
        UtilityRailHost.Visibility = presentation.IsUtilityRailPresented ? Visibility.Visible : Visibility.Collapsed;

        // A splitter only earns its place between two presented surfaces.
        bool showBottomSplitter = isMainPresented && isBottomPresented;
        bool showSideSplitter = isSidePresented && isMainColumnPresented;
        BottomAreaSplitter.Visibility = showBottomSplitter ? Visibility.Visible : Visibility.Collapsed;
        SideAreaSplitter.Visibility = showSideSplitter ? Visibility.Visible : Visibility.Collapsed;
        UtilityPanelSplitter.Visibility = isUtilityPanelPresented ? Visibility.Visible : Visibility.Collapsed;

        var composer = CreateComposer();

        UtilityPanelColumn.MinWidth = composer.UtilityPanelMinimumWidth;
        UtilityPanelHost.MinWidth = composer.UtilityPanelMinimumWidth;
        if (!isUtilityPanelPresented)
        {
            UtilityPanelColumn.Width = new GridLength(0);
        }

        // Main's row is only zeroed to hand its column over to the Bottom area. The Side area can span
        // all three document rows, so zeroing them when it is the only presented area would leave it no
        // height.
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

        MainAreaColumn.Width = isMainColumnPresented ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        if (!isSidePresented)
        {
            SideAreaColumn.Width = new GridLength(0);
        }
        else if (!isMainColumnPresented)
        {
            SideAreaColumn.Width = new GridLength(1, GridUnitType.Star);
        }

        // Every track holds the floor composed for what it is presenting, including the star tracks the Main
        // area sits in: they take whatever the pixel-sized surfaces leave, so a track without a floor of its
        // own is the one that absorbs every shortfall. A surface that is not presented composes to zero, which
        // is what lets the zero track sizes above hold.
        MainAreaRow.MinHeight = composer.MainRowMinimumHeight;
        MainAreaColumn.MinWidth = composer.MainColumnMinimumWidth;
        BottomAreaRow.MinHeight = composer.BottomAreaMinimumHeight;
        SideAreaColumn.MinWidth = composer.SideAreaMinimumWidth;

        ClampPresentedSurfaceSizes();
    }

    /// <summary>
    /// Sets the width of the Utility Panel, the height of the Bottom area or the width of the Side area,
    /// clamped to what the current layout leaves for it. Ignored while the surface is hidden, and for an
    /// area that is the only one presented, because a sole presented area fills the panel.
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
                    UtilityPanelColumn.Width = new GridLength(CreateComposer().ClampUtilityPanelWidth(size));
                }
                break;

            case WorkspaceSurface.BottomArea:
                if (_presentation.IsBottomAreaPresented &&
                    _presentation.IsMainAreaPresented)
                {
                    BottomAreaRow.Height = new GridLength(CreateComposer().ClampBottomAreaHeight(size));
                }
                break;

            case WorkspaceSurface.SideArea:
                if (_presentation.IsSideAreaPresented &&
                    isMainColumnPresented)
                {
                    SideAreaColumn.Width = new GridLength(CreateComposer().ClampSideAreaWidth(size));
                }
                break;
        }
    }

    // A surface holds the size it was given while its peers were different ones, so revealing a surface, or
    // spanning the Bottom area across one, can leave a size the arrangement no longer has room for. Only the
    // pixel-sized tracks are re-clamped: a star-sized track is a sole presented area filling the panel. Each
    // clamp composes against the sizes the ones before it settled at, so the Side area is offered what the
    // Utility Panel has already taken and the two add up to the extent rather than overshooting it.
    private void ClampPresentedSurfaceSizes()
    {
        if (_presentation.IsUtilityPanelPresented &&
            UtilityPanelColumn.Width.IsAbsolute)
        {
            double clampedWidth = CreateComposer().ClampUtilityPanelWidth(UtilityPanelColumn.Width.Value);
            UtilityPanelColumn.Width = new GridLength(clampedWidth);
        }

        if (_presentation.IsBottomAreaPresented &&
            BottomAreaRow.Height.IsAbsolute)
        {
            double clampedHeight = CreateComposer().ClampBottomAreaHeight(BottomAreaRow.Height.Value);
            BottomAreaRow.Height = new GridLength(clampedHeight);
        }

        if (_presentation.IsSideAreaPresented &&
            SideAreaColumn.Width.IsAbsolute)
        {
            double clampedWidth = CreateComposer().ClampSideAreaWidth(SideAreaColumn.Width.Value);
            SideAreaColumn.Width = new GridLength(clampedWidth);
        }
    }

    // Snapshots what the composer needs off the live layout, taken again at every site that asks for a value so
    // a clamp reads the sizes the clamps before it settled at.
    //
    // The extent is this control's own, never the root grid's or its tracks'. On the Skia heads a grid laid out
    // larger than the space it was given reports the size it wanted rather than the size it has, so a maximum
    // measured from it grows with every delta that overflows. This control is arranged by its parent on both
    // heads, so its extent is the space the workspace actually has.
    private WorkspaceSurfaceComposer CreateComposer()
    {
        var metrics = new WorkspaceSurfaceMetrics(
            MainAreaMinimumSize: GetAreaMinimumSize(DocumentArea.Main),
            BottomAreaMinimumSize: GetAreaMinimumSize(DocumentArea.Bottom),
            SideAreaMinimumSize: GetAreaMinimumSize(DocumentArea.Side),
            UtilityPanelMinimumWidth: WorkspaceMinimumSize.ComposeUtilityPanelWidth(),
            UtilityRailWidth: WorkspaceConstants.UtilityRailWidth,
            GutterSize: GutterSize,
            WorkspaceExtent: new Size(ActualWidth, ActualHeight),
            UtilityPanelWidth: ResolveTrackWidth(UtilityPanelColumn.Width),
            SideAreaWidth: ResolveTrackWidth(SideAreaColumn.Width));

        return new WorkspaceSurfaceComposer(_presentation, metrics);
    }

    private Size GetAreaMinimumSize(DocumentArea area)
    {
        if (AreaMinimumSizes is null)
        {
            return new Size(0, 0);
        }

        return AreaMinimumSizes(area);
    }

    // A star-sized track is a sole presented surface filling the workspace rather than a width the composition
    // has to work around, so it reports no width of its own.
    private static double? ResolveTrackWidth(GridLength trackSize)
    {
        if (!trackSize.IsAbsolute)
        {
            return null;
        }

        return trackSize.Value;
    }

    // The channel between two surfaces. The splitter in it takes this size, which is what holds the gap open.
    private static double GutterSize => (double)Application.Current.Resources["GutterSize"];

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

        int utilityPanelRowSpan = spansUtilityPanel ? AboveBottomAreaRowSpan : FullHeightRowSpan;
        Grid.SetRowSpan(UtilityPanelHost, utilityPanelRowSpan);
        Grid.SetRowSpan(UtilityPanelSplitter, utilityPanelRowSpan);

        int sideAreaRowSpan = spansSideArea ? AboveBottomAreaRowSpan : FullHeightRowSpan;
        Grid.SetRowSpan(SideAreaGrid, sideAreaRowSpan);
        Grid.SetRowSpan(SideAreaSplitter, sideAreaRowSpan);

        // The panel meets the application border until the Bottom area runs under it, which puts a
        // gutter below it to draw an edge against.
        _utilityPanel.SetBottomEdgePresented(spansUtilityPanel);
    }

    // Bottom and Side are measured from the far edge, so their deltas are inverted. The Utility Panel is
    // measured from the near edge.
    private void InitializeSurfaceSplitters()
    {
        // Each splitter is held between its surface's own floor and the space the arrangement leaves it, the
        // same pair of values every other site that sizes a surface asks for. Both ends are composed on every
        // delta rather than captured here, so a floor that moves with the layout is honoured.
        _utilityPanelSplitterHelper = new SplitterHelper(
            RootGrid,
            GridResizeMode.Columns,
            UtilityPanelColumnIndex,
            minSizeFunc: () => CreateComposer().UtilityPanelMinimumWidth,
            maxSizeFunc: () => CreateComposer().AvailableUtilityPanelWidth);

        _bottomAreaSplitterHelper = new SplitterHelper(
            RootGrid,
            GridResizeMode.Rows,
            BottomAreaRowIndex,
            minSizeFunc: () => CreateComposer().BottomAreaMinimumHeight,
            invertDelta: true,
            maxSizeFunc: () => CreateComposer().AvailableBottomAreaHeight)
        {
            SnapTargets = () => BottomAreaSplitterSnapTargets?.Invoke() ?? Array.Empty<double>()
        };

        _sideAreaSplitterHelper = new SplitterHelper(
            RootGrid,
            GridResizeMode.Columns,
            SideAreaColumnIndex,
            minSizeFunc: () => CreateComposer().SideAreaMinimumWidth,
            invertDelta: true,
            maxSizeFunc: () => CreateComposer().AvailableSideAreaWidth);

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
