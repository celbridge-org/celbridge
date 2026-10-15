using Celbridge.UserInterface.Helpers;
using Windows.Graphics;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests the window placement rules. A saved placement must leave enough of the title bar on screen to be seen
/// and dragged. Saved bounds move and shrink to fit a work area. A window with no saved placement opens centred in
/// the work area.
/// </summary>
[TestFixture]
public class WindowPlacementPolicyTests
{
    private static readonly RectInt32 Display = Rect(0, 0, 3840, 2160);
    private static readonly SizeInt32 MinimumSize = new SizeInt32 { Width = 1080, Height = 680 };

    private static RectInt32 Rect(int x, int y, int width, int height)
    {
        return new RectInt32
        {
            X = x,
            Y = y,
            Width = width,
            Height = height
        };
    }

    private static bool IsUsable(RectInt32 windowBounds)
    {
        var titleBarStrip = WindowPlacementPolicy.GetTitleBarStrip(windowBounds);
        return WindowPlacementPolicy.IsTitleBarUsable(titleBarStrip, Display);
    }

    [Test]
    public void WindowWellInsideDisplay_IsUsable()
    {
        IsUsable(Rect(100, 100, 2464, 1936)).Should().BeTrue();
    }

    [Test]
    public void WindowSliverInsideDisplayEdge_IsNotUsable()
    {
        // The placement that hid the window: 11 pixels on screen, the rest off the right edge.
        IsUsable(Rect(3829, -17, 2464, 1936)).Should().BeFalse();
    }

    [Test]
    public void WindowOnDisconnectedDisplay_IsNotUsable()
    {
        IsUsable(Rect(5000, 100, 2464, 1936)).Should().BeFalse();
    }

    [Test]
    public void WindowPartlyAboveDisplay_IsUsableWhileEnoughTitleBarRemains()
    {
        IsUsable(Rect(100, -17, 2464, 1936)).Should().BeTrue();
        IsUsable(Rect(100, -25, 2464, 1936)).Should().BeFalse();
    }

    [Test]
    public void DefaultBounds_TakeTheirShareOfTheWorkAreaCentred()
    {
        // A 1920 x 1080 display, less the menu bar and the Dock.
        var bounds = WindowPlacementPolicy.ComposeDefaultBounds(Rect(0, 30, 1920, 976), MinimumSize);

        bounds.Should().Be(Rect(192, 127, 1536, 781));
    }

    [Test]
    public void DefaultBounds_AreNeverSmallerThanTheMinimumSize()
    {
        var bounds = WindowPlacementPolicy.ComposeDefaultBounds(Rect(0, 25, 1280, 775), MinimumSize);

        bounds.Should().Be(Rect(100, 72, 1080, 680));
    }

    [Test]
    public void DefaultBounds_AreNeverLargerThanTheWorkArea()
    {
        var bounds = WindowPlacementPolicy.ComposeDefaultBounds(Rect(0, 0, 1000, 600), MinimumSize);

        bounds.Should().Be(Rect(0, 0, 1000, 600));
    }

    [Test]
    public void SavedBoundsInsideTheWorkArea_AreKept()
    {
        var bounds = WindowPlacementPolicy.FitInWorkArea(Rect(192, 128, 1536, 781), [Rect(0, 30, 1920, 976)]);

        bounds.Should().Be(Rect(192, 128, 1536, 781));
    }

    [Test]
    public void SavedBoundsLargerThanTheWorkArea_ShrinkToFitIt()
    {
        // Bounds saved in physical pixels on a display at scale 2.
        var bounds = WindowPlacementPolicy.FitInWorkArea(Rect(0, 134, 2160, 1360), [Rect(0, 30, 1920, 976)]);

        bounds.Should().Be(Rect(0, 30, 1920, 976));
    }

    [Test]
    public void SavedBoundsPastTheWorkAreaEdge_MoveInsideIt()
    {
        var bounds = WindowPlacementPolicy.FitInWorkArea(Rect(1500, 600, 1080, 680), [Rect(0, 30, 1920, 976)]);

        bounds.Should().Be(Rect(840, 326, 1080, 680));
    }

    [Test]
    public void SavedBounds_FitTheWorkAreaTheyOverlapMost()
    {
        var workAreas = new[] { Rect(0, 30, 1920, 976), Rect(1920, 0, 2560, 1415) };

        var bounds = WindowPlacementPolicy.FitInWorkArea(Rect(1800, 100, 1200, 800), workAreas);

        bounds.Should().Be(Rect(1920, 100, 1200, 800));
    }

    [Test]
    public void DefaultBounds_FollowTheWorkAreaOnASecondDisplay()
    {
        var bounds = WindowPlacementPolicy.ComposeDefaultBounds(Rect(1920, -200, 2560, 1415), MinimumSize);

        bounds.Should().Be(Rect(2176, -59, 2048, 1132));
    }
}
