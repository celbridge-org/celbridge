using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Reads and sets the application window's bounds for saving and restoring. Also places a window that has no
/// usable saved bounds. macOS works on the native window's frame in points. Every other head works on the
/// AppWindow, in physical pixels. Bounds have a top-left origin on every head.
/// </summary>
public interface IWindowPlacement
{
    /// <summary>
    /// Returns the window's current bounds, title bar included.
    /// </summary>
    RectInt32 GetBounds(AppWindow appWindow);

    /// <summary>
    /// Moves and resizes the window to saved bounds. macOS fits the bounds inside a screen's work area first.
    /// </summary>
    void Restore(AppWindow appWindow, RectInt32 bounds);

    /// <summary>
    /// Places a window that has no usable saved bounds. macOS centres the window in the work area of the window's
    /// screen. Every other head keeps the placement the system gives a new window. The minimum size is in the same
    /// unit as the bounds.
    /// </summary>
    void ApplyDefault(AppWindow appWindow, SizeInt32 minimumSize);
}
