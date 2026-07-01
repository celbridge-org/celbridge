using Windows.Graphics;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Decides whether a saved window placement is still usable by testing the title-bar strip against the
/// available displays. The display geometry is read differently per head: the packaged WinAppSDK head
/// uses DisplayArea, while the Skia desktop heads (where DisplayArea is unavailable) use native screen
/// geometry on macOS and cannot validate elsewhere.
/// </summary>
public interface IWindowBoundsValidator
{
    /// <summary>
    /// Whether any part of the window's title-bar strip falls within a display's work area, meaning the
    /// saved bounds are safe to restore. Returns false when the placement cannot be validated, so the
    /// caller falls back to the default placement.
    /// </summary>
    bool IsTitleBarVisible(RectInt32 windowBounds);
}
