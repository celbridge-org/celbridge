using Celbridge.Settings;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Views;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests the composition behind the workspace minimum sizes, and that the floors it composes from still fit
/// inside the minimum window size.
/// </summary>
[TestFixture]
public class WorkspaceMinimumSizeTests
{
    // The channel between two surfaces, mirroring the GutterSize resource in Styles.xaml, which a test cannot
    // resolve without an application.
    private const double GutterSize = 7;

    [Test]
    public void DefaultLayout_FitsWithinTheMinimumWindowSize()
    {
        var defaultVisibleSurfaces = SettingCatalog.Layout.PreferredSurfaceVisibility.DefaultValue;

        var workspaceMinimumSize = WorkspaceMinimumSize.ComposeDefaultLayout(defaultVisibleSurfaces, GutterSize);

        // The minimum window size covers the whole window, so the workspace only gets what is left of it after
        // the application toolbar above and the window's own chrome around both.
        double windowWidth = workspaceMinimumSize.Width + WindowStateHelper.WindowFrameWidth;
        double windowHeight = workspaceMinimumSize.Height +
            ApplicationToolbar.ToolbarHeight +
            WindowStateHelper.WindowFrameHeight;

        windowWidth.Should().BeLessThanOrEqualTo(WindowStateHelper.MinimumWindowWidth);
        windowHeight.Should().BeLessThanOrEqualTo(WindowStateHelper.MinimumWindowHeight);
    }

    [Test]
    public void DefaultLayout_ComposesEverySurfaceItShows()
    {
        double sectionWidth = WorkspaceConstants.DocumentMinWidth + WorkspaceConstants.SectionEdgeThickness * 2;
        double sectionHeight = WorkspaceConstants.DocumentMinHeight +
            WorkspaceConstants.SectionTabStripHeight +
            WorkspaceConstants.SectionEdgeThickness * 2;

        var minimumSize = WorkspaceMinimumSize.ComposeDefaultLayout(WorkspaceSurface.All, GutterSize);

        // The Utility Panel, the Main area and the Side area across, with a channel between each pair.
        double utilityPanelWidth = WorkspaceConstants.UtilityPanelRailWidth + GutterSize + sectionWidth;
        minimumSize.Width.Should().Be(utilityPanelWidth + GutterSize + sectionWidth + GutterSize + sectionWidth);

        // The Main area above the Bottom area, and the channel above them both.
        minimumSize.Height.Should().Be(GutterSize + sectionHeight + GutterSize + sectionHeight);
    }

    [Test]
    public void DefaultLayout_DropsASurfaceItDoesNotShowAndTheChannelWithIt()
    {
        double sectionWidth = WorkspaceConstants.DocumentMinWidth + WorkspaceConstants.SectionEdgeThickness * 2;

        var mainAreaOnly = WorkspaceMinimumSize.ComposeDefaultLayout(WorkspaceSurface.None, GutterSize);

        mainAreaOnly.Width.Should().Be(sectionWidth);
    }
}
