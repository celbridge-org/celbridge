using Windows.Graphics;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Rules for placing the application window. The rules decide whether a saved placement is usable and how saved
/// bounds fit a work area. The rules also place a window that has no saved placement.
/// </summary>
internal static class WindowPlacementPolicy
{
    private const int TitleBarHeight = 40;

    // A window with no saved placement takes this share of the work area's width and height.
    private const double DefaultSizeFraction = 0.8;

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

    /// <summary>
    /// Returns the bounds of a window with no saved placement. The window is centred in the work area, at a set
    /// share of the work area's size. The size stays between the minimum size and the work area's size. The
    /// bounds use the work area's origin, top-left or bottom-left, since centring is the same in both.
    /// </summary>
    public static RectInt32 ComposeDefaultBounds(RectInt32 workArea, SizeInt32 minimumSize)
    {
        int width = FitDefaultLength(workArea.Width, minimumSize.Width);
        int height = FitDefaultLength(workArea.Height, minimumSize.Height);

        return new RectInt32
        {
            X = workArea.X + (workArea.Width - width) / 2,
            Y = workArea.Y + (workArea.Height - height) / 2,
            Width = width,
            Height = height
        };
    }

    /// <summary>
    /// Returns saved bounds moved and shrunk to fit inside the work area that overlaps them most. Bounds that
    /// overlap every work area equally fit the first work area.
    /// </summary>
    public static RectInt32 FitInWorkArea(RectInt32 bounds, IReadOnlyList<RectInt32> workAreas)
    {
        var workArea = workAreas
            .OrderByDescending(area => OverlapArea(bounds, area))
            .First();

        int width = Math.Min(bounds.Width, workArea.Width);
        int height = Math.Min(bounds.Height, workArea.Height);

        return new RectInt32
        {
            X = Math.Clamp(bounds.X, workArea.X, workArea.X + workArea.Width - width),
            Y = Math.Clamp(bounds.Y, workArea.Y, workArea.Y + workArea.Height - height),
            Width = width,
            Height = height
        };
    }

    private static long OverlapArea(RectInt32 first, RectInt32 second)
    {
        long width = Math.Min(first.X + first.Width, second.X + second.Width) - Math.Max(first.X, second.X);
        long height = Math.Min(first.Y + first.Height, second.Y + second.Height) - Math.Max(first.Y, second.Y);
        return Math.Max(width, 0) * Math.Max(height, 0);
    }

    private static int FitDefaultLength(int available, int minimum)
    {
        int preferred = (int)Math.Round(available * DefaultSizeFraction);
        return Math.Min(Math.Max(preferred, minimum), available);
    }
}
