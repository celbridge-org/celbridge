using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Constrains how small the user can resize the application window. The mechanism differs per head: the
/// packaged WinAppSDK head constrains the overlapped presenter, while the Skia desktop heads constrain the
/// native window on macOS and have no equivalent elsewhere.
/// </summary>
public interface IWindowSizeConstraints
{
    /// <summary>
    /// Applies the smallest size the user may resize the window to. Does nothing on heads that cannot
    /// express the constraint, leaving the window unconstrained.
    /// </summary>
    void ApplyMinimumSize(AppWindow appWindow, SizeInt32 minimumSize);
}
