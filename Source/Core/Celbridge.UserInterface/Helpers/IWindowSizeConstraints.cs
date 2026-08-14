using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Constrains how small the user can resize the application window. The mechanism differs per head: macOS
/// constrains the native window, and every other head constrains the overlapped presenter.
/// </summary>
public interface IWindowSizeConstraints
{
    /// <summary>
    /// Applies the smallest size the user may resize the window to, in the unit that head measures its window
    /// in, which IPlatformInfo.WindowSizesUsePhysicalPixels tells the caller.
    /// </summary>
    void ApplyMinimumSize(AppWindow appWindow, SizeInt32 minimumSize);
}
