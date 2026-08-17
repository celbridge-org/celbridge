using Celbridge.Documents.Helpers;
using Celbridge.Documents.Services;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Views.Controls;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Helpers;
using Celbridge.WorkspaceUI.Views;
using Windows.Foundation;

namespace Celbridge.Documents.Views;

/// <summary>
/// The geometry of the three document areas inside the grids the workspace surface container provides:
/// which areas are presented, how each one is split, the splitter that divides a split area, the floors its
/// sections are held at, and the chrome they draw. The sections and the documents in them belong to
/// DocumentSectionContainer, which this asks for a section view whenever it needs one.
/// </summary>
public sealed class DocumentAreaLayout
{
    private const double MinDragDistance = 5.0; // Minimum pixels to count as a real drag

    private readonly WorkspaceSurfaceContainer _surfaceContainer;
    private readonly Func<DocumentSection, DocumentSectionView> _sectionLookup;
    private readonly Action<DocumentArea> _migrateSecondarySection;
    private readonly AreaLayoutState _layoutState = new();
    private readonly SectionChromeCalculator _chromeCalculator;
    private readonly Dictionary<DocumentArea, Splitter> _splitSplitters = new();
    private readonly Dictionary<DocumentArea, SplitterHelper> _splitHelpers = new();
    private readonly Dictionary<DocumentArea, UIElement> _areaToolbars = new();

    private double _totalDragDelta = 0;

    /// <summary>
    /// Event raised when an area's split state or split position changes.
    /// </summary>
    public event Action<DocumentArea, bool, double>? AreaLayoutChanged;

    /// <summary>
    /// The sections that are currently mounted, in reading order.
    /// </summary>
    public IReadOnlyList<DocumentSection> VisibleSections => _layoutState.VisibleSections;

    /// <summary>
    /// The sections a document can be selected from, which ignores any isolation so closing the last
    /// document in an isolated area can move to one elsewhere.
    /// </summary>
    public IEnumerable<DocumentSection> SelectableSections => _layoutState.SelectableSections;

    /// <summary>
    /// The area currently shown on its own, or null when the areas are laid out normally.
    /// </summary>
    public DocumentArea? IsolatedArea => _layoutState.IsolatedArea;

    // Folding an area migrates its secondary section's tabs into its primary one before the area is rebuilt,
    // so no tab is left in an unmounted section. That is the section container's work, so it is passed in.
    public DocumentAreaLayout(
        WorkspaceSurfaceContainer surfaceContainer,
        Func<DocumentSection, DocumentSectionView> sectionLookup,
        Action<DocumentArea> migrateSecondarySection)
    {
        _surfaceContainer = surfaceContainer;
        _sectionLookup = sectionLookup;
        _migrateSecondarySection = migrateSecondarySection;

        _surfaceContainer.BottomAreaSplitterSnapTargets = ResolveBottomAreaSnapTargets;

        // An area's minimum derives from the sections inside its grid, which this class lays out, so the
        // surface container asks for it rather than naming a size of its own.
        _surfaceContainer.AreaMinimumSizes = GetAreaMinimumSize;

        _chromeCalculator = new SectionChromeCalculator(_layoutState);

        foreach (var area in DocumentLayoutHelper.AllAreas)
        {
            RebuildArea(area);
            WatchAreaSize(area);
        }

        ApplyWorkspaceLayout();

        _sectionLookup(DocumentArea.Main.GetPrimarySection()).Loaded += OnPrimarySectionLoaded;
    }

    /// <summary>
    /// Whether the area is currently showing both of its sections.
    /// </summary>
    public bool IsAreaSplit(DocumentArea area)
    {
        return _layoutState.IsAreaSplit(area);
    }

    /// <summary>
    /// Gets the share of a split area taken by its primary section.
    /// </summary>
    public double GetAreaSplitRatio(DocumentArea area)
    {
        return _layoutState.GetAreaSplitRatio(area);
    }

    /// <summary>
    /// The smallest size the area can be laid out at, or zero while it is not presented. A split area holds
    /// two sections along its split axis, with a gutter between them.
    /// </summary>
    public Size GetAreaMinimumSize(DocumentArea area)
    {
        if (!_layoutState.IsAreaPresented(area))
        {
            return new Size(0, 0);
        }

        return WorkspaceMinimumSize.ComposeArea(
            SectionMinimumSize,
            isSplit: _layoutState.IsAreaSplit(area),
            splitsHorizontally: area.SplitsHorizontally(),
            gutterSize: GutterSize);
    }

    /// <summary>
    /// Whether a document in the area can be moved into a new split: the area must be unsplit, have room
    /// for two sections, and hold more than one document so the split does not empty its primary section.
    /// </summary>
    public bool CanStartAreaSplit(DocumentArea area)
    {
        int primaryTabCount = _sectionLookup(area.GetPrimarySection()).TabCount;

        return _layoutState.CanStartSplit(area, CanSplitArea(area), primaryTabCount);
    }

    /// <summary>
    /// Shows a single area filling the whole panel, hiding the other two, or restores the normal layout
    /// when passed null. The isolated area keeps its own split, and every area's visibility, size and
    /// split state are left untouched underneath, so clearing the isolation restores what the user had.
    /// </summary>
    public void SetIsolatedArea(DocumentArea? area)
    {
        if (_layoutState.SetIsolatedArea(area))
        {
            ApplyWorkspaceLayout();
        }
    }

    /// <summary>
    /// Sets whether the Utility Panel is showing alongside the document areas, which decides whether the
    /// areas draw their left edge or leave it flush against the application border.
    /// </summary>
    public void SetUtilityPanelPresented(bool isPresented)
    {
        if (_layoutState.SetUtilityPanelPresented(isPresented))
        {
            ApplyWorkspaceLayout();
        }
    }

    /// <summary>
    /// Sets how far the Bottom area spans across the workspace: the Main area only, or across the Utility
    /// Panel, the Side area, or both. The surfaces it runs across stop above it.
    /// </summary>
    public void SetBottomAreaAlignment(BottomAreaAlignment alignment)
    {
        if (_layoutState.SetBottomAreaAlignment(alignment))
        {
            ApplyWorkspaceLayout();
        }
    }

    /// <summary>
    /// Shows or hides an area. Hiding leaves its sections and their tabs intact, so the documents in a
    /// collapsed area stay open and reappear where they were. Main is always visible.
    /// </summary>
    public void SetAreaVisible(DocumentArea area, bool isVisible)
    {
        if (_layoutState.SetAreaVisible(area, isVisible))
        {
            ApplyWorkspaceLayout();
        }
    }

    /// <summary>
    /// Splits the area into two sections, or folds its secondary section back into the primary one.
    /// Folding migrates the secondary section's tabs rather than closing them.
    /// </summary>
    public void SetAreaSplit(DocumentArea area, bool isSplit)
    {
        if (!_layoutState.SetAreaSplit(area, isSplit))
        {
            return;
        }

        if (!isSplit)
        {
            _migrateSecondarySection(area);
        }

        RebuildArea(area);

        // A split area needs room for two sections, so its minimum grows, and the workspace floors the
        // surface container composes from it are re-applied by pushing the presentation again.
        ApplyWorkspaceLayout();

        AreaLayoutChanged?.Invoke(area, isSplit, _layoutState.GetAreaSplitRatio(area));
    }

    /// <summary>
    /// Sets the share of a split area taken by its primary section.
    /// </summary>
    public void SetAreaSplitRatio(DocumentArea area, double ratio)
    {
        if (!_layoutState.SetAreaSplitRatio(area, ratio))
        {
            return;
        }

        if (_layoutState.IsAreaSplit(area))
        {
            ApplySplitRatio(area);
        }
    }

    /// <summary>
    /// Folds a split area back when either of its sections has run out of documents, so a split section is
    /// never left empty. The surviving documents always end up in the primary section.
    /// </summary>
    public void ReconcileAreaSplit(DocumentArea area)
    {
        int primaryTabCount = _sectionLookup(area.GetPrimarySection()).TabCount;
        int secondaryTabCount = _sectionLookup(area.GetSecondarySection()).TabCount;

        // Unsplitting migrates the secondary section's tabs into the primary one, which covers both
        // cases: an empty secondary migrates nothing, an empty primary receives everything.
        if (_layoutState.ShouldFoldSplit(area, primaryTabCount, secondaryTabCount))
        {
            SetAreaSplit(area, false);
        }
    }

    /// <summary>
    /// Sets the toolbar hosted in an area's tab strip. The toolbar is re-placed on the section nearest the
    /// area's inner corner whenever that area is rebuilt.
    /// </summary>
    public void SetAreaToolbar(DocumentArea area, UIElement toolbar)
    {
        _areaToolbars[area] = toolbar;
        PlaceAreaToolbar(area);
    }

    /// <summary>
    /// Folds every area back to a single section and restores equal split positions.
    /// </summary>
    public async Task ResetAreaLayoutAsync()
    {
        foreach (var area in DocumentLayoutHelper.AllAreas)
        {
            _layoutState.SetAreaSplitRatio(area, AreaLayoutState.DefaultSplitRatio);
            SetAreaSplit(area, false);
        }

        var tcs = new TaskCompletionSource<bool>();

        // Wait for layout to complete so callers that persist the result read settled state.
        GetAreaGrid(DocumentArea.Main).DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var area in DocumentLayoutHelper.AllAreas)
            {
                AreaLayoutChanged?.Invoke(area, false, AreaLayoutState.DefaultSplitRatio);
            }

            tcs.SetResult(true);
        });

        await tcs.Task;
    }

    // The section chrome is measured from the tab strip the section template builds, which has no size until
    // the section has laid out, so every minimum composed above stands on an unmeasured strip. Re-applies them
    // on the cycle after the load, by which point the strip has been measured.
    private void OnPrimarySectionLoaded(object sender, RoutedEventArgs e)
    {
        var primarySectionView = _sectionLookup(DocumentArea.Main.GetPrimarySection());
        primarySectionView.Loaded -= OnPrimarySectionLoaded;

        _ = primarySectionView.DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var area in DocumentLayoutHelper.AllAreas)
            {
                ApplySectionTrackMinimums(area);
            }

            ApplyWorkspaceLayout();
        });
    }

    // An area that shrinks below the room for two sections can no longer be split, which the tab context
    // menu reflects.
    private void WatchAreaSize(DocumentArea area)
    {
        var areaGrid = GetAreaGrid(area);
        areaGrid.SizeChanged += (s, e) =>
        {
            UpdateSectionMoveTargets(area);
        };
    }

    // Whether the area currently has room for two sections at their minimum size.
    private bool CanSplitArea(DocumentArea area)
    {
        var areaGrid = GetAreaGrid(area);
        var splitMinimum = WorkspaceMinimumSize.ComposeArea(
            SectionMinimumSize,
            isSplit: true,
            splitsHorizontally: area.SplitsHorizontally(),
            gutterSize: GutterSize);

        if (area.SplitsHorizontally())
        {
            return areaGrid.ActualWidth >= splitMinimum.Width;
        }

        return areaGrid.ActualHeight >= splitMinimum.Height;
    }

    // Every section is built from the same template, so the Main area's primary section, the one section that
    // is always mounted, measures the chrome on behalf of all of them.
    private Size SectionMinimumSize => _sectionLookup(DocumentArea.Main.GetPrimarySection()).MinimumSize;

    // The channel between two surfaces. The splitter in it takes this size, which is what holds the gap open.
    private static double GutterSize => (double)Application.Current.Resources["GutterSize"];

    // The floor a split area's sections are held at, along the axis its split splitter moves.
    private double ResolveSplitSectionMinimum(DocumentArea area)
    {
        if (area.SplitsHorizontally())
        {
            return SectionMinimumSize.Width;
        }

        return SectionMinimumSize.Height;
    }

    private Grid GetAreaGrid(DocumentArea area)
    {
        return _surfaceContainer.GetAreaGrid(area);
    }

    // Rebuilds an area's internal grid for its current split state. Sections that stay mounted are left
    // attached: reparenting a section resets its TabView measurement and leaves the tab strip stuck in
    // an overflow-scroll state until the next real resize.
    private void RebuildArea(DocumentArea area)
    {
        var areaGrid = GetAreaGrid(area);
        bool isSplit = _layoutState.IsAreaSplit(area);
        bool isHorizontal = area.SplitsHorizontally();

        var primarySectionView = _sectionLookup(area.GetPrimarySection());
        var secondarySectionView = _sectionLookup(area.GetSecondarySection());

        if (_splitSplitters.TryGetValue(area, out var existingSplitter))
        {
            existingSplitter.DragStarted -= Splitter_DragStarted;
            existingSplitter.DragDelta -= Splitter_DragDelta;
            existingSplitter.DragCompleted -= Splitter_DragCompleted;
            existingSplitter.DoubleClicked -= Splitter_DoubleClicked;
            areaGrid.Children.Remove(existingSplitter);
            _splitSplitters.Remove(area);
            _splitHelpers.Remove(area);
        }

        if (!isSplit &&
            areaGrid.Children.Contains(secondarySectionView))
        {
            areaGrid.Children.Remove(secondarySectionView);
        }

        areaGrid.ColumnDefinitions.Clear();
        areaGrid.RowDefinitions.Clear();

        double ratio = _layoutState.GetAreaSplitRatio(area);

        if (isHorizontal)
        {
            areaGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(isSplit ? ratio : 1, GridUnitType.Star)
            });
        }
        else
        {
            areaGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(isSplit ? ratio : 1, GridUnitType.Star)
            });
        }

        SetSectionPosition(primarySectionView, isHorizontal, 0);
        if (!areaGrid.Children.Contains(primarySectionView))
        {
            areaGrid.Children.Add(primarySectionView);
        }

        if (isSplit)
        {
            if (isHorizontal)
            {
                areaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                areaGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1 - ratio, GridUnitType.Star)
                });
            }
            else
            {
                areaGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                areaGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(1 - ratio, GridUnitType.Star)
                });
            }

            var splitter = CreateSplitSplitter(area, isHorizontal);
            SetSectionPosition(splitter, isHorizontal, 1);
            areaGrid.Children.Add(splitter);
            _splitSplitters[area] = splitter;

            SetSectionPosition(secondarySectionView, isHorizontal, 2);
            if (!areaGrid.Children.Contains(secondarySectionView))
            {
                areaGrid.Children.Add(secondarySectionView);
            }
        }

        ApplySectionTrackMinimums(area);
        UpdateSectionMoveTargets(area);
        PlaceAreaToolbar(area);
        ApplyAreaSectionChrome(area);
    }

    // Puts the composed section minimum on the tracks the area's sections sit in. The gutter track between
    // them is auto sized and carries none of its own.
    private void ApplySectionTrackMinimums(DocumentArea area)
    {
        var areaGrid = GetAreaGrid(area);
        var sectionMinimum = SectionMinimumSize;

        if (area.SplitsHorizontally())
        {
            foreach (var columnDefinition in areaGrid.ColumnDefinitions)
            {
                if (columnDefinition.Width.IsStar)
                {
                    columnDefinition.MinWidth = sectionMinimum.Width;
                }
            }

            return;
        }

        foreach (var rowDefinition in areaGrid.RowDefinitions)
        {
            if (rowDefinition.Height.IsStar)
            {
                rowDefinition.MinHeight = sectionMinimum.Height;
            }
        }
    }

    private static void SetSectionPosition(FrameworkElement element, bool isHorizontal, int index)
    {
        if (isHorizontal)
        {
            Grid.SetColumn(element, index);
            Grid.SetRow(element, 0);
        }
        else
        {
            Grid.SetRow(element, index);
            Grid.SetColumn(element, 0);
        }
    }

    private Splitter CreateSplitSplitter(DocumentArea area, bool isHorizontal)
    {
        var splitter = new Splitter
        {
            // A horizontally split area is divided by a vertical splitter, and the reverse.
            Orientation = isHorizontal ? Orientation.Vertical : Orientation.Horizontal,
            Tag = area
        };

        splitter.DragStarted += Splitter_DragStarted;
        splitter.DragDelta += Splitter_DragDelta;
        splitter.DragCompleted += Splitter_DragCompleted;
        splitter.DoubleClicked += Splitter_DoubleClicked;

        return splitter;
    }

    // Places an area's toolbar on the section nearest that area's inner corner, in the end of that section's
    // tab strip nearest the same corner: the top-left of the Side area, and the top-right of the Bottom area,
    // whose right-hand section takes it while it is split.
    private void PlaceAreaToolbar(DocumentArea area)
    {
        if (!_areaToolbars.TryGetValue(area, out var toolbar))
        {
            return;
        }

        var primarySectionView = _sectionLookup(area.GetPrimarySection());
        var secondarySectionView = _sectionLookup(area.GetSecondarySection());

        bool toolbarOnSecondary = area.SplitsHorizontally() && _layoutState.IsAreaSplit(area);

        var hostSectionView = toolbarOnSecondary ? secondarySectionView : primarySectionView;
        var clearedSectionView = toolbarOnSecondary ? primarySectionView : secondarySectionView;

        // Both slots are cleared on both sections: the area a toolbar sits in can change which end it takes,
        // and a section can lose the toolbar to its sibling.
        clearedSectionView.SetTabStripHeader(null);
        clearedSectionView.SetTabStripFooter(null);

        if (area.PlacesToolbarAtStripStart())
        {
            hostSectionView.SetTabStripFooter(null);
            hostSectionView.SetTabStripHeader(toolbar);

            return;
        }

        hostSectionView.SetTabStripHeader(null);
        hostSectionView.SetTabStripFooter(toolbar);
    }

    // Pushes the area state the tab context menu needs down onto its tabs: whether the area is split, and
    // whether it has room to be.
    private void UpdateSectionMoveTargets(DocumentArea area)
    {
        bool isSplit = _layoutState.IsAreaSplit(area);
        bool canSplit = CanSplitArea(area);

        foreach (var section in area.GetSections())
        {
            var sectionView = _sectionLookup(section);
            sectionView.IsAreaSplit = isSplit;
            sectionView.CanSplitArea = canSplit;
        }
    }

    private void ApplySplitRatio(DocumentArea area)
    {
        var areaGrid = GetAreaGrid(area);
        double ratio = _layoutState.GetAreaSplitRatio(area);

        if (area.SplitsHorizontally())
        {
            if (areaGrid.ColumnDefinitions.Count == 3)
            {
                areaGrid.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
                areaGrid.ColumnDefinitions[2].Width = new GridLength(1 - ratio, GridUnitType.Star);
            }
        }
        else
        {
            if (areaGrid.RowDefinitions.Count == 3)
            {
                areaGrid.RowDefinitions[0].Height = new GridLength(ratio, GridUnitType.Star);
                areaGrid.RowDefinitions[2].Height = new GridLength(1 - ratio, GridUnitType.Star);
            }
        }
    }

    // Pushes the current presentation to the surface container, then re-applies the section chrome,
    // which depends on which surfaces the sections now face.
    private void ApplyWorkspaceLayout()
    {
        var presentation = new WorkspaceSurfacePresentation(
            IsMainAreaPresented: _layoutState.IsAreaPresented(DocumentArea.Main),
            IsBottomAreaPresented: _layoutState.IsAreaPresented(DocumentArea.Bottom),
            IsSideAreaPresented: _layoutState.IsAreaPresented(DocumentArea.Side),
            IsUtilityPanelPresented: _layoutState.IsUtilityPanelPresented,
            BottomAreaSpansUtilityPanel: _layoutState.BottomAreaSpansUtilityPanel,
            BottomAreaSpansSideArea: _layoutState.BottomAreaSpansSideArea);

        _surfaceContainer.ApplyPresentation(presentation);

        ApplySectionChrome();
    }

    // A section is the rectangle a document actually sits in, so the chrome is drawn per section rather than
    // per area: the gutter splitting one area into two divides two such rectangles, exactly as the gutter
    // between two areas does.
    private void ApplySectionChrome()
    {
        foreach (var area in DocumentLayoutHelper.AllAreas)
        {
            ApplyAreaSectionChrome(area);
        }
    }

    // Splitting an area moves an inner edge onto a section that did not have one, so this runs on every
    // rebuild rather than only when the root grid layout changes.
    private void ApplyAreaSectionChrome(DocumentArea area)
    {
        double cornerRadius = (double)Application.Current.Resources["PanelCornerRadius"];
        var areaChrome = _chromeCalculator.CalculateAreaChrome(area, cornerRadius);

        var primarySectionView = _sectionLookup(area.GetPrimarySection());
        primarySectionView.SetGutterChrome(areaChrome.Primary.Edges, areaChrome.Primary.Corners);

        if (areaChrome.Secondary is SectionChrome secondaryChrome)
        {
            var secondarySectionView = _sectionLookup(area.GetSecondarySection());
            secondarySectionView.SetGutterChrome(secondaryChrome.Edges, secondaryChrome.Corners);
        }
    }

    private void Splitter_DragStarted(object? sender, EventArgs e)
    {
        if (sender is not Splitter splitter || splitter.Tag is not DocumentArea area)
        {
            return;
        }

        _totalDragDelta = 0;

        if (!_splitHelpers.TryGetValue(area, out var helper))
        {
            var areaGrid = GetAreaGrid(area);
            var mode = area.SplitsHorizontally() ? GridResizeMode.Columns : GridResizeMode.Rows;
            helper = new SplitterHelper(areaGrid, mode, 0, 2, minSizeFunc: () => ResolveSplitSectionMinimum(area))
            {
                SnapTargets = () => ResolveSplitSnapTargets(area)
            };
            _splitHelpers[area] = helper;
        }

        helper.OnDragStarted();
    }

    // The size an area's primary section takes when its split divider lines up with the one it pairs with.
    // Main and Bottom share a grid column, so their dividers align when their primary sections are the same
    // width. The Side area starts at the same row as Main, so its divider is measured from the same origin
    // as the Main/Bottom boundary and aligns when its primary section matches the Main area's height. An
    // alignment that runs the Bottom area under the Side area leaves nothing to line up with, so it has no
    // target.
    private IReadOnlyList<double> ResolveSplitSnapTargets(DocumentArea area)
    {
        if (area == DocumentArea.Side)
        {
            if (!_layoutState.IsAreaPresented(DocumentArea.Main) ||
                !_layoutState.IsAreaPresented(DocumentArea.Bottom) ||
                _layoutState.BottomAreaSpansSideArea)
            {
                return Array.Empty<double>();
            }

            return new[]
            {
                GetAreaGrid(DocumentArea.Main).ActualHeight
            };
        }

        var partnerArea = area == DocumentArea.Main ? DocumentArea.Bottom : DocumentArea.Main;

        if (!_layoutState.IsAreaPresented(partnerArea) ||
            !_layoutState.IsAreaSplit(partnerArea))
        {
            return Array.Empty<double>();
        }

        var partnerGrid = GetAreaGrid(partnerArea);

        if (partnerGrid.ColumnDefinitions.Count == 0)
        {
            return Array.Empty<double>();
        }

        return new[]
        {
            partnerGrid.ColumnDefinitions[0].ActualWidth
        };
    }

    // The Bottom area height that puts the Main/Bottom boundary level with the Side area's split divider.
    // That divider is measured down from the top of the Main area while the Bottom splitter sizes the
    // Bottom area up from the base, so the target is the two areas' height less the Side area's primary
    // section. An alignment that runs the Bottom area under the Side area leaves nothing to line up with.
    // Supplied to the surface container, whose splitter owns the snapping.
    private IReadOnlyList<double> ResolveBottomAreaSnapTargets()
    {
        if (!_layoutState.IsAreaPresented(DocumentArea.Side) ||
            !_layoutState.IsAreaSplit(DocumentArea.Side) ||
            _layoutState.BottomAreaSpansSideArea)
        {
            return Array.Empty<double>();
        }

        var sideAreaGrid = GetAreaGrid(DocumentArea.Side);

        if (sideAreaGrid.RowDefinitions.Count == 0)
        {
            return Array.Empty<double>();
        }

        double sidePrimaryHeight = sideAreaGrid.RowDefinitions[0].ActualHeight;
        double documentRowsHeight = GetAreaGrid(DocumentArea.Main).ActualHeight +
            GetAreaGrid(DocumentArea.Bottom).ActualHeight;
        double alignedBottomHeight = documentRowsHeight - sidePrimaryHeight;

        return new[]
        {
            alignedBottomHeight
        };
    }

    private void Splitter_DragDelta(object? sender, double delta)
    {
        if (sender is not Splitter splitter || splitter.Tag is not DocumentArea area)
        {
            return;
        }

        _totalDragDelta += Math.Abs(delta);

        if (_splitHelpers.TryGetValue(area, out var helper))
        {
            helper.OnDragDelta(delta);
        }
    }

    private void Splitter_DragCompleted(object? sender, EventArgs e)
    {
        if (sender is not Splitter splitter || splitter.Tag is not DocumentArea area)
        {
            return;
        }

        // Skip if no significant drag occurred (e.g., just a click without dragging)
        if (_totalDragDelta < MinDragDistance)
        {
            return;
        }

        double ratio = MeasureSplitRatio(area);
        if (ratio > 0 && ratio < 1)
        {
            _layoutState.SetAreaSplitRatio(area, ratio);

            // Convert back to proportional Star sizing so the split holds its share as the area resizes.
            ApplySplitRatio(area);

            AreaLayoutChanged?.Invoke(area, true, ratio);
        }
    }

    private void Splitter_DoubleClicked(object? sender, EventArgs e)
    {
        if (sender is not Splitter splitter || splitter.Tag is not DocumentArea area)
        {
            return;
        }

        _layoutState.SetAreaSplitRatio(area, AreaLayoutState.DefaultSplitRatio);
        ApplySplitRatio(area);

        AreaLayoutChanged?.Invoke(area, true, AreaLayoutState.DefaultSplitRatio);
    }

    // The share of the area currently taken by its primary section, measured from the settled grid.
    private double MeasureSplitRatio(DocumentArea area)
    {
        var areaGrid = GetAreaGrid(area);

        double primarySize;
        double secondarySize;

        if (area.SplitsHorizontally())
        {
            if (areaGrid.ColumnDefinitions.Count != 3)
            {
                return 0;
            }
            primarySize = areaGrid.ColumnDefinitions[0].ActualWidth;
            secondarySize = areaGrid.ColumnDefinitions[2].ActualWidth;
        }
        else
        {
            if (areaGrid.RowDefinitions.Count != 3)
            {
                return 0;
            }
            primarySize = areaGrid.RowDefinitions[0].ActualHeight;
            secondarySize = areaGrid.RowDefinitions[2].ActualHeight;
        }

        double total = primarySize + secondarySize;
        if (total <= 0)
        {
            return 0;
        }

        return primarySize / total;
    }
}
