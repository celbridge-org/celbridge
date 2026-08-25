using Celbridge.UserInterface.Helpers;
using Celbridge.WorkspaceUI.Helpers;
using Windows.Foundation;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// Tests the workspace-level composition: the floor each surface track is held at, the workspace floor
/// composed from them, and the clamps every entry point that sizes a surface goes through.
/// </summary>
[TestFixture]
public class WorkspaceSurfaceComposerTests
{
    // The channel between two surfaces, mirroring the GutterSize resource in Styles.xaml, which a test cannot
    // resolve without an application.
    private const double GutterSize = 7;

    // Stand-ins for the minimums the surfaces report. The Bottom area is as wide as a split one and the Side
    // area as tall, so each Bottom area alignment composes a different result.
    private static readonly Size MainAreaMinimum = new(240, 210);
    private static readonly Size BottomAreaMinimum = new(487, 200);
    private static readonly Size SideAreaMinimum = new(220, 430);
    private const double UtilityPanelMinimum = 280;
    private const double UtilityRailWidth = 40;

    // A workspace with room to spare, so the clamps are deciding how the surplus is shared out rather than
    // running out of space.
    private const double WorkspaceWidth = 1200;
    private const double WorkspaceHeight = 900;

    // A stored size from a larger window, which every entry point has to hold back to what still fits.
    private const double StoredOversizedSize = 900;

    [Test]
    public void MinimumSize_ComposesEverySurfaceItPresents()
    {
        var composer = CreateComposer(CreatePresentation());

        // The Main and Bottom areas share a column, so it holds the wider of the two, with the Utility Panel
        // and the Side area beside it, and the rail down the left of the lot. The rail meets the panel
        // directly, so no channel is counted between those two.
        double expectedWidth = UtilityRailWidth +
            UtilityPanelMinimum +
            GutterSize +
            BottomAreaMinimum.Width +
            GutterSize +
            SideAreaMinimum.Width;
        composer.MinimumSize.Width.Should().Be(expectedWidth);

        // The Side area runs the full height beside the Main area above the Bottom one, and is the taller of
        // the two, with the channel above the document areas on top.
        composer.MinimumSize.Height.Should().Be(SideAreaMinimum.Height + GutterSize);
    }

    [Test]
    public void MinimumSize_DropsAHiddenUtilityPanelAndTheChannelWithIt()
    {
        var composer = CreateComposer(CreatePresentation(isUtilityPanelPresented: false));

        // The rail is not part of the panel, so collapsing the panel leaves the rail holding the left of the
        // workspace on its own.
        composer.UtilityPanelMinimumWidth.Should().Be(0);
        composer.MinimumSize.Width.Should().Be(
            UtilityRailWidth + BottomAreaMinimum.Width + GutterSize + SideAreaMinimum.Width);
    }

    [Test]
    public void MinimumSize_DropsAHiddenUtilityRail()
    {
        var composer = CreateComposer(CreatePresentation(isUtilityRailPresented: false));

        double expectedWidth = UtilityPanelMinimum +
            GutterSize +
            BottomAreaMinimum.Width +
            GutterSize +
            SideAreaMinimum.Width;
        composer.MinimumSize.Width.Should().Be(expectedWidth);
    }

    [Test]
    public void MinimumSize_DropsAHiddenBottomAreaAndTheChannelWithIt()
    {
        var composer = CreateComposer(CreatePresentation(isBottomAreaPresented: false));

        // The Main area is left holding its shared column on its own.
        composer.BottomAreaMinimumHeight.Should().Be(0);
        composer.MainColumnMinimumWidth.Should().Be(MainAreaMinimum.Width);

        double expectedWidth = UtilityRailWidth +
            UtilityPanelMinimum +
            GutterSize +
            MainAreaMinimum.Width +
            GutterSize +
            SideAreaMinimum.Width;
        composer.MinimumSize.Width.Should().Be(expectedWidth);
    }

    [Test]
    public void MinimumSize_GrowsWithASplitArea()
    {
        // A split area holds two sections along its split axis, with a gutter between them.
        var splitMainAreaMinimum = new Size(
            MainAreaMinimum.Width + GutterSize + MainAreaMinimum.Width,
            MainAreaMinimum.Height);

        var composer = CreateComposer(
            CreatePresentation(isBottomAreaPresented: false),
            mainAreaMinimumSize: splitMainAreaMinimum);

        composer.MainColumnMinimumWidth.Should().Be(splitMainAreaMinimum.Width);

        double expectedWidth = UtilityRailWidth +
            UtilityPanelMinimum +
            GutterSize +
            splitMainAreaMinimum.Width +
            GutterSize +
            SideAreaMinimum.Width;
        composer.MinimumSize.Width.Should().Be(expectedWidth);
    }

    [Test]
    public void BottomAreaSpanningTheUtilityPanel_StopsSettingTheSharedColumnFloor()
    {
        var composer = CreateComposer(CreatePresentation(bottomAreaSpansUtilityPanel: true));

        composer.MainColumnMinimumWidth.Should().Be(MainAreaMinimum.Width);

        double expectedWidth = UtilityRailWidth +
            UtilityPanelMinimum +
            GutterSize +
            MainAreaMinimum.Width +
            GutterSize +
            SideAreaMinimum.Width;
        composer.MinimumSize.Width.Should().Be(expectedWidth);
    }

    [Test]
    public void BottomAreaSpanningTheSideArea_MovesTheSideAreaIntoTheMainRow()
    {
        var composer = CreateComposer(CreatePresentation(bottomAreaSpansSideArea: true));

        // The Side area stops above the Bottom area, so it stands beside the Main area rather than beside the
        // pair, and the row they share holds the taller of the two.
        composer.MainRowMinimumHeight.Should().Be(SideAreaMinimum.Height);

        double expectedHeight = SideAreaMinimum.Height +
            GutterSize +
            BottomAreaMinimum.Height +
            GutterSize;
        composer.MinimumSize.Height.Should().Be(expectedHeight);

        // The Bottom area takes the Side area's column, so the row it sits in is the wider of the two.
        composer.MinimumSize.Width.Should().Be(
            UtilityRailWidth + UtilityPanelMinimum + GutterSize + BottomAreaMinimum.Width);
    }

    [Test]
    public void BottomAreaSpanningBothNeighbours_ComposesAgainstTheWholeWorkspace()
    {
        var composer = CreateComposer(
            CreatePresentation(bottomAreaSpansUtilityPanel: true, bottomAreaSpansSideArea: true));

        composer.MainColumnMinimumWidth.Should().Be(MainAreaMinimum.Width);
        composer.MainRowMinimumHeight.Should().Be(SideAreaMinimum.Height);

        // Nothing sits beside the Bottom area, so the surfaces above it set the width and its own extent only
        // takes over once it is the wider of the two.
        double expectedWidth = UtilityRailWidth +
            UtilityPanelMinimum +
            GutterSize +
            MainAreaMinimum.Width +
            GutterSize +
            SideAreaMinimum.Width;
        composer.MinimumSize.Width.Should().Be(expectedWidth);
    }

    [TestCaseSource(nameof(LayoutConfigurations))]
    public void ComposedFloors_FitInsideTheWorkspaceMinimum(WorkspaceSurfacePresentation presentation)
    {
        var composer = CreateComposer(presentation);

        // The floors written onto the workspace grid's tracks, with the channels between them. A track held
        // above what the workspace minimum budgets for it is the disagreement a drag past its limit was: the
        // maximum is composed from the minimum, so it would allow more than the tracks will give up.
        double documentAreaWidths = WorkspaceMinimumSize.ComposeAdjacent(
            composer.MainColumnMinimumWidth,
            composer.SideAreaMinimumWidth,
            GutterSize);
        double railWidth = presentation.IsUtilityRailPresented ? UtilityRailWidth : 0;
        double trackWidths = railWidth + WorkspaceMinimumSize.ComposeAdjacent(
            composer.UtilityPanelMinimumWidth,
            documentAreaWidths,
            GutterSize);

        double trackHeights = WorkspaceMinimumSize.ComposeAdjacent(
            composer.MainRowMinimumHeight,
            composer.BottomAreaMinimumHeight,
            GutterSize) + GutterSize;

        trackWidths.Should().BeLessThanOrEqualTo(composer.MinimumSize.Width);
        trackHeights.Should().BeLessThanOrEqualTo(composer.MinimumSize.Height);
    }

    [Test]
    public void Drag_LeavesTheMainAreaAtItsFloor()
    {
        var composer = CreateComposer(
            CreatePresentation(),
            WorkspaceWidth,
            WorkspaceHeight,
            utilityPanelWidth: 300,
            sideAreaWidth: 300);

        // A drag is refused below the surface's own floor and clamped at the space the arrangement leaves it,
        // so the widest the Utility Panel can be dragged is what the maximum reports.
        double draggedWidth = composer.AvailableUtilityPanelWidth;

        double mainColumnWidth = ResolveMainColumnWidth(WorkspaceWidth, draggedWidth, 300);
        mainColumnWidth.Should().BeGreaterThanOrEqualTo(composer.MainColumnMinimumWidth);
    }

    [Test]
    public void Restore_LeavesTheMainAreaAtItsFloor()
    {
        var presentation = CreatePresentation();

        // The stored sizes are applied one surface at a time, so the Side area is offered what the Utility
        // Panel settled at rather than what it was holding before the restore.
        var utilityPanelComposer = CreateComposer(
            presentation,
            WorkspaceWidth,
            WorkspaceHeight,
            utilityPanelWidth: 300,
            sideAreaWidth: 300);
        double restoredUtilityPanelWidth = utilityPanelComposer.ClampUtilityPanelWidth(StoredOversizedSize);

        var sideAreaComposer = CreateComposer(
            presentation,
            WorkspaceWidth,
            WorkspaceHeight,
            utilityPanelWidth: restoredUtilityPanelWidth,
            sideAreaWidth: 300);
        double restoredSideAreaWidth = sideAreaComposer.ClampSideAreaWidth(StoredOversizedSize);

        double mainColumnWidth = ResolveMainColumnWidth(
            WorkspaceWidth,
            restoredUtilityPanelWidth,
            restoredSideAreaWidth);
        mainColumnWidth.Should().BeGreaterThanOrEqualTo(sideAreaComposer.MainColumnMinimumWidth);
    }

    [Test]
    public void Restore_HoldsTheBottomAreaToWhatTheWorkspaceHasDownIt()
    {
        var composer = CreateComposer(CreatePresentation(), WorkspaceWidth, WorkspaceHeight);

        double restoredHeight = composer.ClampBottomAreaHeight(StoredOversizedSize);

        // The Bottom area is the only resizable surface down the workspace, so nothing is held back from it
        // and the Main area's row keeps exactly its floor.
        double mainRowHeight = WorkspaceHeight - restoredHeight - GutterSize - GutterSize;
        mainRowHeight.Should().BeGreaterThanOrEqualTo(composer.MainRowMinimumHeight);
    }

    [Test]
    public void Reveal_LeavesTheMainAreaAtItsFloor()
    {
        // The Utility Panel was dragged as wide as the workspace allowed while the Side area was hidden.
        var hiddenSideArea = CreatePresentation(isSideAreaPresented: false);
        var hiddenSideAreaComposer = CreateComposer(
            hiddenSideArea,
            WorkspaceWidth,
            WorkspaceHeight,
            utilityPanelWidth: 300,
            sideAreaWidth: 0);
        double widenedUtilityPanelWidth = hiddenSideAreaComposer.AvailableUtilityPanelWidth;

        // Revealing the Side area re-clamps the pixel-sized surfaces against the arrangement the reveal
        // produced, which is what the panel's width no longer fits.
        var revealed = CreatePresentation();
        var utilityPanelComposer = CreateComposer(
            revealed,
            WorkspaceWidth,
            WorkspaceHeight,
            utilityPanelWidth: widenedUtilityPanelWidth,
            sideAreaWidth: 0);
        double clampedUtilityPanelWidth = utilityPanelComposer.ClampUtilityPanelWidth(widenedUtilityPanelWidth);

        var sideAreaComposer = CreateComposer(
            revealed,
            WorkspaceWidth,
            WorkspaceHeight,
            utilityPanelWidth: clampedUtilityPanelWidth,
            sideAreaWidth: 0);
        double revealedSideAreaWidth = sideAreaComposer.ClampSideAreaWidth(0);

        double mainColumnWidth = ResolveMainColumnWidth(
            WorkspaceWidth,
            clampedUtilityPanelWidth,
            revealedSideAreaWidth);
        mainColumnWidth.Should().BeGreaterThanOrEqualTo(sideAreaComposer.MainColumnMinimumWidth);
    }

    [Test]
    public void Resize_LeavesTheMainAreaAtItsFloor()
    {
        var presentation = CreatePresentation();

        // The window has shrunk to exactly the workspace floor, and the stored sizes are applied again against
        // what it now has to divide.
        double shrunkWidth = CreateComposer(presentation).MinimumSize.Width;

        var utilityPanelComposer = CreateComposer(
            presentation,
            shrunkWidth,
            WorkspaceHeight,
            utilityPanelWidth: 500,
            sideAreaWidth: 400);
        double narrowedUtilityPanelWidth = utilityPanelComposer.ClampUtilityPanelWidth(500);

        var sideAreaComposer = CreateComposer(
            presentation,
            shrunkWidth,
            WorkspaceHeight,
            utilityPanelWidth: narrowedUtilityPanelWidth,
            sideAreaWidth: 400);
        double narrowedSideAreaWidth = sideAreaComposer.ClampSideAreaWidth(400);

        double mainColumnWidth = ResolveMainColumnWidth(
            shrunkWidth,
            narrowedUtilityPanelWidth,
            narrowedSideAreaWidth);
        mainColumnWidth.Should().BeGreaterThanOrEqualTo(sideAreaComposer.MainColumnMinimumWidth);
    }

    [Test]
    public void BelowTheWorkspaceFloor_EverySurfaceHoldsItsOwnFloor()
    {
        var presentation = CreatePresentation();
        double clippedWidth = CreateComposer(presentation).MinimumSize.Width - 100;

        var composer = CreateComposer(
            presentation,
            clippedWidth,
            WorkspaceHeight,
            utilityPanelWidth: 500,
            sideAreaWidth: 400);

        // There is no arrangement that holds every surface, so the resizable ones come back to their floors
        // and the shortfall is clipped rather than shared out.
        composer.ClampUtilityPanelWidth(500).Should().Be(composer.UtilityPanelMinimumWidth);
        composer.ClampSideAreaWidth(400).Should().Be(composer.SideAreaMinimumWidth);
    }

    [Test]
    public void BeforeTheWorkspaceIsLaidOut_OnlyTheFloorApplies()
    {
        var composer = CreateComposer(CreatePresentation());

        composer.ClampUtilityPanelWidth(StoredOversizedSize).Should().Be(StoredOversizedSize);
        composer.ClampUtilityPanelWidth(100).Should().Be(composer.UtilityPanelMinimumWidth);
    }

    // Every arrangement the workspace can be in: each Bottom area alignment, and each surface hidden.
    private static IEnumerable<WorkspaceSurfacePresentation> LayoutConfigurations()
    {
        yield return CreatePresentation();
        yield return CreatePresentation(bottomAreaSpansUtilityPanel: true);
        yield return CreatePresentation(bottomAreaSpansSideArea: true);
        yield return CreatePresentation(bottomAreaSpansUtilityPanel: true, bottomAreaSpansSideArea: true);
        yield return CreatePresentation(isUtilityPanelPresented: false);
        yield return CreatePresentation(isUtilityRailPresented: false);
        yield return CreatePresentation(isBottomAreaPresented: false);
        yield return CreatePresentation(isSideAreaPresented: false);
        yield return CreatePresentation(
            isBottomAreaPresented: false,
            isSideAreaPresented: false,
            isUtilityPanelPresented: false);
    }

    // What the Main area's column is left with once the rail and the pixel-sized surfaces either side of it
    // have taken their share of the workspace.
    private static double ResolveMainColumnWidth(
        double workspaceWidth,
        double utilityPanelWidth,
        double sideAreaWidth)
    {
        return workspaceWidth - UtilityRailWidth - utilityPanelWidth - GutterSize - sideAreaWidth - GutterSize;
    }

    private static WorkspaceSurfacePresentation CreatePresentation(
        bool isBottomAreaPresented = true,
        bool isSideAreaPresented = true,
        bool isUtilityPanelPresented = true,
        bool isUtilityRailPresented = true,
        bool bottomAreaSpansUtilityPanel = false,
        bool bottomAreaSpansSideArea = false)
    {
        return new WorkspaceSurfacePresentation(
            IsMainAreaPresented: true,
            IsBottomAreaPresented: isBottomAreaPresented,
            IsSideAreaPresented: isSideAreaPresented,
            IsUtilityPanelPresented: isUtilityPanelPresented,
            IsUtilityRailPresented: isUtilityRailPresented,
            BottomAreaSpansUtilityPanel: bottomAreaSpansUtilityPanel,
            BottomAreaSpansSideArea: bottomAreaSpansSideArea);
    }

    // A workspace extent of zero stands for one that has not been laid out yet, where only the floors apply.
    private static WorkspaceSurfaceComposer CreateComposer(
        WorkspaceSurfacePresentation presentation,
        double workspaceWidth = 0,
        double workspaceHeight = 0,
        double? utilityPanelWidth = null,
        double? sideAreaWidth = null,
        Size? mainAreaMinimumSize = null)
    {
        var metrics = new WorkspaceSurfaceMetrics(
            MainAreaMinimumSize: mainAreaMinimumSize ?? MainAreaMinimum,
            BottomAreaMinimumSize: BottomAreaMinimum,
            SideAreaMinimumSize: SideAreaMinimum,
            UtilityPanelMinimumWidth: UtilityPanelMinimum,
            UtilityRailWidth: UtilityRailWidth,
            GutterSize: GutterSize,
            WorkspaceExtent: new Size(workspaceWidth, workspaceHeight),
            UtilityPanelWidth: utilityPanelWidth,
            SideAreaWidth: sideAreaWidth);

        return new WorkspaceSurfaceComposer(presentation, metrics);
    }
}
