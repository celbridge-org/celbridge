using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Windows.Foundation;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests the primitives every workspace minimum is composed from: the section floor, the area floor above
/// it, and the floor of the layout a workspace opens with.
/// </summary>
[TestFixture]
public class WorkspaceMinimumSizeTests
{
    // The channel between two surfaces, mirroring the GutterSize resource in Styles.xaml, which a test cannot
    // resolve without an application.
    private const double GutterSize = 7;

    [Test]
    public void ComposeSection_AddsTheMeasuredChromeToTheDocumentFloor()
    {
        // The chrome is the one measured term in the composition, so the section passes it in rather than the
        // composition assuming it.
        var sectionChrome = new Size(4, 60);

        var sectionMinimum = WorkspaceMinimumSize.ComposeSection(sectionChrome);

        sectionMinimum.Width.Should().Be(WorkspaceConstants.DocumentMinWidth + sectionChrome.Width);
        sectionMinimum.Height.Should().Be(WorkspaceConstants.DocumentMinHeight + sectionChrome.Height);
    }

    [Test]
    public void ComposeArea_TakesTheSectionFloorWhileUnsplit()
    {
        var sectionMinimum = new Size(232, 242);

        var areaMinimum = WorkspaceMinimumSize.ComposeArea(
            sectionMinimum,
            isSplit: false,
            splitsHorizontally: true,
            gutterSize: GutterSize);

        areaMinimum.Should().Be(sectionMinimum);
    }

    [Test]
    public void ComposeArea_DoublesTheSectionFloorAlongTheSplitAxis()
    {
        var sectionMinimum = new Size(232, 242);

        var horizontalSplit = WorkspaceMinimumSize.ComposeArea(
            sectionMinimum,
            isSplit: true,
            splitsHorizontally: true,
            gutterSize: GutterSize);
        var verticalSplit = WorkspaceMinimumSize.ComposeArea(
            sectionMinimum,
            isSplit: true,
            splitsHorizontally: false,
            gutterSize: GutterSize);

        horizontalSplit.Width.Should().Be(sectionMinimum.Width + GutterSize + sectionMinimum.Width);
        horizontalSplit.Height.Should().Be(sectionMinimum.Height);

        verticalSplit.Width.Should().Be(sectionMinimum.Width);
        verticalSplit.Height.Should().Be(sectionMinimum.Height + GutterSize + sectionMinimum.Height);
    }

    [Test]
    public void ComposeAdjacent_DropsTheChannelBesideASurfaceThatIsNotPresented()
    {
        WorkspaceMinimumSize.ComposeAdjacent(100, 50, GutterSize).Should().Be(157);
        WorkspaceMinimumSize.ComposeAdjacent(0, 50, GutterSize).Should().Be(50);
        WorkspaceMinimumSize.ComposeAdjacent(100, 0, GutterSize).Should().Be(100);
    }

    [Test]
    public void SpaceForSurface_OffersTheSurfaceWhateverTheContainerHasBeyondItsMinimum()
    {
        WorkspaceMinimumSize.SpaceForSurface(containerExtent: 1000, containerMinimum: 800, surfaceMinimum: 200)
            .Should().Be(400);

        // Below the container's own minimum the space has run out for every surface at once, so the surface
        // comes back to its floor and the excess is clipped instead.
        WorkspaceMinimumSize.SpaceForSurface(containerExtent: 700, containerMinimum: 800, surfaceMinimum: 200)
            .Should().Be(200);
    }

    [Test]
    public void ComposeDefaultLayout_ComposesEverySurfaceItShows()
    {
        double sectionWidth = WorkspaceConstants.DocumentMinWidth + WorkspaceConstants.SectionEdgeThickness * 2;
        double sectionHeight = WorkspaceConstants.DocumentMinHeight +
            WorkspaceConstants.SectionTabStripHeight +
            WorkspaceConstants.SectionEdgeThickness * 2;

        var minimumSize = WorkspaceMinimumSize.ComposeDefaultLayout(WorkspaceSurface.All, GutterSize);

        // The Utility Panel, the Main area and the Side area across, with a channel between each pair, and the
        // Utility Rail down the left of them. The rail meets the panel directly, so no channel is counted
        // between those two.
        double expectedWidth = WorkspaceConstants.UtilityRailWidth +
            sectionWidth + GutterSize + sectionWidth + GutterSize + sectionWidth;
        minimumSize.Width.Should().Be(expectedWidth);

        // The Main area above the Bottom area. The application toolbar carries the channel above them.
        minimumSize.Height.Should().Be(sectionHeight + GutterSize + sectionHeight);
    }

    [Test]
    public void ComposeDefaultLayout_DropsASurfaceItDoesNotShowAndTheChannelWithIt()
    {
        double sectionWidth = WorkspaceConstants.DocumentMinWidth + WorkspaceConstants.SectionEdgeThickness * 2;

        var mainAreaOnly = WorkspaceMinimumSize.ComposeDefaultLayout(WorkspaceSurface.None, GutterSize);

        // The rail is chrome rather than a surface, so it is still there once every surface has gone.
        mainAreaOnly.Width.Should().Be(WorkspaceConstants.UtilityRailWidth + sectionWidth);
    }
}
