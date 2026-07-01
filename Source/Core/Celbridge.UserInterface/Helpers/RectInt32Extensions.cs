using Windows.Graphics;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Geometry helpers for the Windows.Graphics RectInt32 struct, which carries no methods of its own.
/// </summary>
internal static class RectInt32Extensions
{
    /// <summary>
    /// Whether two rectangles overlap. Rectangles that only touch along an edge do not count as
    /// overlapping.
    /// </summary>
    public static bool IntersectsWith(this RectInt32 rect, RectInt32 other)
    {
        return rect.X < other.X + other.Width &&
               rect.X + rect.Width > other.X &&
               rect.Y < other.Y + other.Height &&
               rect.Y + rect.Height > other.Y;
    }
}
