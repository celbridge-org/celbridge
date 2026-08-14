using Celbridge.Settings;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests that the authored window minimum is the budget the workspace layout floors fit inside, and that the
/// applied size is scaled into the unit the head measures its window in.
/// </summary>
[TestFixture]
public class WindowMinimumSizeTests
{
    // The channel between two surfaces, mirroring the GutterSize resource in Styles.xaml, which a test cannot
    // resolve without an application.
    private const double GutterSize = 7;

    [TestCaseSource(nameof(SurfaceVisibilityCombinations))]
    public void DefaultLayout_FitsInsideTheAuthoredWindowMinimum(WorkspaceSurface visibleSurfaces)
    {
        var windowSize = WindowMinimumSize.ComposeDefaultLayoutWindow(visibleSurfaces, GutterSize);

        windowSize.Width.Should().BeLessThanOrEqualTo(WindowMinimumSize.AuthoredWidth);
        windowSize.Height.Should().BeLessThanOrEqualTo(WindowMinimumSize.AuthoredHeight);
    }

    [Test]
    public void Compose_HoldsTheAuthoredSizeWhileTheDefaultLayoutFitsInsideIt()
    {
        var defaultVisibleSurfaces = SettingCatalog.Layout.PreferredSurfaceVisibility.DefaultValue;

        var minimumSize = WindowMinimumSize.Compose(defaultVisibleSurfaces, GutterSize, windowSizeScale: 1);

        minimumSize.Width.Should().Be(WindowMinimumSize.AuthoredWidth);
        minimumSize.Height.Should().Be(WindowMinimumSize.AuthoredHeight);
    }

    [Test]
    public void Compose_ScalesIntoTheUnitTheHeadMeasuresItsWindowIn()
    {
        var defaultVisibleSurfaces = SettingCatalog.Layout.PreferredSurfaceVisibility.DefaultValue;

        // The presenter enforces the constraint in physical pixels and does not scale them itself, so a size
        // composed in device-independent pixels is scaled before it is applied.
        var minimumSize = WindowMinimumSize.Compose(defaultVisibleSurfaces, GutterSize, windowSizeScale: 2);

        minimumSize.Width.Should().Be(WindowMinimumSize.AuthoredWidth * 2);
        minimumSize.Height.Should().Be(WindowMinimumSize.AuthoredHeight * 2);
    }

    // Every layout a workspace can open with. The window minimum is composed before any workspace exists, so
    // it stands on the authored terms and the stored surface visibility alone.
    private static IEnumerable<WorkspaceSurface> SurfaceVisibilityCombinations()
    {
        yield return WorkspaceSurface.None;
        yield return WorkspaceSurface.UtilityPanel;
        yield return WorkspaceSurface.BottomArea;
        yield return WorkspaceSurface.SideArea;
        yield return WorkspaceSurface.UtilityPanel | WorkspaceSurface.BottomArea;
        yield return WorkspaceSurface.UtilityPanel | WorkspaceSurface.SideArea;
        yield return WorkspaceSurface.BottomArea | WorkspaceSurface.SideArea;
        yield return WorkspaceSurface.All;
    }
}
