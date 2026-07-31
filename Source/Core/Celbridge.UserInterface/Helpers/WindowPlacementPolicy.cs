using Windows.Graphics;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Shared rules for deciding whether a saved window placement is usable, applied by the per-platform
/// window bounds validators.
/// </summary>
internal static class WindowPlacementPolicy
{
    private const int TitleBarHeight = 40;

    // Enough of the title bar must land on a screen for the user to see the window and drag it back. A
    // window nudged just past a screen edge still intersects that screen by a few pixels, which reads as
    // visible to a bare intersection test but leaves nothing on screen to grab.
    private const int MinimumVisibleTitleBarWidth = 120;
    private const int MinimumVisibleTitleBarHeight = 20;

    /// <summary>
    /// The title bar strip along the top of the given window bounds.
    /// </summary>
    public static RectInt32 GetTitleBarStrip(RectInt32 windowBounds)
    {
        return new RectInt32
        {
            X = windowBounds.X,
            Y = windowBounds.Y,
            Width = windowBounds.Width,
            Height = TitleBarHeight
        };
    }

    /// <summary>
    /// Whether enough of the title bar strip falls inside the given screen area for the window to be
    /// seen and dragged.
    /// </summary>
    public static bool IsTitleBarUsable(RectInt32 titleBarStrip, RectInt32 screenArea)
    {
        return titleBarStrip.OverlapsAtLeast(
            screenArea,
            MinimumVisibleTitleBarWidth,
            MinimumVisibleTitleBarHeight);
    }
}
