using Celbridge.UserInterface.Helpers;
using Windows.Graphics;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests the rule the window bounds validators use to decide whether a saved window placement leaves
/// enough of the title bar on screen to be seen and dragged.
/// </summary>
[TestFixture]
public class WindowPlacementPolicyTests
{
    private static readonly RectInt32 Display = Rect(0, 0, 3840, 2160);

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
}
