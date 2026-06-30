using Celbridge.Logging;
using Celbridge.UserInterface.Helpers.FullScreen;
using Microsoft.UI.Windowing;

namespace Celbridge.UserInterface.Platform.FullScreen;

/// <summary>
/// Fullscreen controller for the macOS Skia desktop head. macOS owns fullscreen natively through the
/// title-bar green button (the app adopts the native window chrome), so the app does not emulate it:
/// EnterFullScreen and ExitFullScreen are no-ops and IsFullScreen reports the OS's native-fullscreen
/// state. The app's non-windowed layout modes (Zen, Presenter) only change panel visibility on macOS.
/// The user fills the screen with the native control.
/// </summary>
public sealed class MacDesktopFullScreenController : IFullScreenController
{
    private readonly ILogger<MacDesktopFullScreenController> _logger;

    public MacDesktopFullScreenController(ILogger<MacDesktopFullScreenController> logger)
    {
        _logger = logger;
    }

    public bool IsFullScreen => GetNativeFullScreenState();

    public void Initialize(AppWindow appWindow)
    {
    }

    public Result EnterFullScreen()
    {
        _logger.LogDebug("Fullscreen is handled natively on macOS; the app does not emulate it");
        return Result.Ok();
    }

    public Result ExitFullScreen()
    {
        return Result.Ok();
    }

    private static bool GetNativeFullScreenState()
    {
#if !WINDOWS
        return MacOSWindowInterop.IsMainWindowInNativeFullScreen();
#else
        return false;
#endif
    }
}
