using Windows.Graphics;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Geometry helpers for the Windows.Graphics RectInt32 struct, which carries no methods of its own.
/// </summary>
internal static class RectInt32Extensions
{
    /// <summary>
    /// Whether two rectangles overlap by at least the given width and height. Rectangles that only touch
    /// along an edge do not count as overlapping.
    /// </summary>
    public static bool OverlapsAtLeast(this RectInt32 rect, RectInt32 other, int minimumWidth, int minimumHeight)
    {
        int left = Math.Max(rect.X, other.X);
        int top = Math.Max(rect.Y, other.Y);
        int right = Math.Min(rect.X + rect.Width, other.X + other.Width);
        int bottom = Math.Min(rect.Y + rect.Height, other.Y + other.Height);

        int overlapWidth = right - left;
        int overlapHeight = bottom - top;

        return overlapWidth >= minimumWidth &&
               overlapHeight >= minimumHeight;
    }
}
