using Celbridge.UserInterface.Helpers;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests that the authored window minimum is the budget the workspace layout floors fit inside, and that the
/// applied size is scaled into the unit the head measures its window in.
/// </summary>
[TestFixture]
public class WindowMinimumSizeTests
{
    // The channel between two areas, mirroring the GutterSize resource in Styles.xaml, which a test cannot
    // resolve without an application.
    private const double GutterSize = 7;

    [TestCaseSource(nameof(AreaVisibilityCombinations))]
    public void DefaultLayout_FitsInsideTheAuthoredWindowMinimum(IReadOnlySet<WorkspaceArea> visibleAreas)
    {
        var windowSize = WindowMinimumSize.ComposeDefaultLayoutWindow(visibleAreas, GutterSize);

        windowSize.Width.Should().BeLessThanOrEqualTo(WindowMinimumSize.AuthoredWidth);
        windowSize.Height.Should().BeLessThanOrEqualTo(WindowMinimumSize.AuthoredHeight);
    }

    [Test]
    public void Compose_HoldsTheAuthoredSizeWhileTheDefaultLayoutFitsInsideIt()
    {
        var minimumSize = WindowMinimumSize.Compose(
            WorkspaceAreaHelper.AllAreasVisible,
            GutterSize,
            windowSizeScale: 1);

        minimumSize.Width.Should().Be(WindowMinimumSize.AuthoredWidth);
        minimumSize.Height.Should().Be(WindowMinimumSize.AuthoredHeight);
    }

    [Test]
    public void Compose_ScalesIntoTheUnitTheHeadMeasuresItsWindowIn()
    {
        // The presenter enforces the constraint in physical pixels and does not scale them itself, so a size
        // composed in device-independent pixels is scaled before it is applied.
        var minimumSize = WindowMinimumSize.Compose(
            WorkspaceAreaHelper.AllAreasVisible,
            GutterSize,
            windowSizeScale: 2);

        minimumSize.Width.Should().Be(WindowMinimumSize.AuthoredWidth * 2);
        minimumSize.Height.Should().Be(WindowMinimumSize.AuthoredHeight * 2);
    }

    // Every layout a workspace can open with. The window minimum is composed before any workspace exists, so
    // it stands on the authored terms and the stored area visibility alone. Main is in every case because it
    // is always visible.
    private static IEnumerable<IReadOnlySet<WorkspaceArea>> AreaVisibilityCombinations()
    {
        yield return VisibleAreas();
        yield return VisibleAreas(WorkspaceArea.Utility);
        yield return VisibleAreas(WorkspaceArea.Bottom);
        yield return VisibleAreas(WorkspaceArea.Side);
        yield return VisibleAreas(WorkspaceArea.Utility, WorkspaceArea.Bottom);
        yield return VisibleAreas(WorkspaceArea.Utility, WorkspaceArea.Side);
        yield return VisibleAreas(WorkspaceArea.Bottom, WorkspaceArea.Side);
        yield return WorkspaceAreaHelper.AllAreasVisible;
    }

    private static IReadOnlySet<WorkspaceArea> VisibleAreas(params WorkspaceArea[] collapsibleAreas)
    {
        var visibleAreas = new HashSet<WorkspaceArea>(collapsibleAreas)
        {
            WorkspaceArea.Main
        };

        return visibleAreas;
    }
}
