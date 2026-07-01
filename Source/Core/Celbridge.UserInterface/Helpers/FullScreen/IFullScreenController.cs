using Microsoft.UI.Windowing;

namespace Celbridge.UserInterface.Helpers.FullScreen;

/// <summary>
/// Applies and exits OS-level fullscreen for the main window. The mechanism is platform-specific:
/// the packaged WinAppSDK head uses the native fullscreen presenter, while the Skia desktop heads
/// emulate fullscreen because the WPF shell's fullscreen presenter has no visual effect.
/// </summary>
public interface IFullScreenController
{
    /// <summary>
    /// Whether the window is currently presented in fullscreen.
    /// </summary>
    bool IsFullScreen { get; }

    /// <summary>
    /// Binds the controller to the main window's AppWindow. Must be called once before entering fullscreen.
    /// </summary>
    void Initialize(AppWindow appWindow);

    /// <summary>
    /// Presents the window in fullscreen, remembering the prior windowed placement so it can be restored.
    /// </summary>
    Result EnterFullScreen();

    /// <summary>
    /// Exits fullscreen and restores the window to its prior windowed placement.
    /// </summary>
    Result ExitFullScreen();
}
