using Celbridge.Logging;
using Celbridge.UserInterface.Helpers.FullScreen;
using Microsoft.UI.Windowing;

namespace Celbridge.UserInterface.Platform.FullScreen;

/// <summary>
/// Fullscreen controller for the macOS Skia desktop head. The app adopts the native window chrome, so fullscreen is
/// the native one. EnterFullScreen and ExitFullScreen move the window into and out of native fullscreen. The user can
/// also change fullscreen with the title-bar green button or the View menu. The controller reports every change the
/// window makes with WindowFullScreenChangedMessage.
/// </summary>
public sealed class MacDesktopFullScreenController : IFullScreenController
{
    private readonly ILogger<MacDesktopFullScreenController> _logger;
    private readonly IMessengerService _messengerService;

    // Cached on the UI thread whenever the window changes, so reads from other threads never call into AppKit.
    private bool _isFullScreen;

    public MacDesktopFullScreenController(
        ILogger<MacDesktopFullScreenController> logger,
        IMessengerService messengerService)
    {
        _logger = logger;
        _messengerService = messengerService;
    }

    public bool IsFullScreen => _isFullScreen;

    public void Initialize(AppWindow appWindow)
    {
        _isFullScreen = GetNativeFullScreenState();
        appWindow.Changed += OnAppWindowChanged;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        var isFullScreen = GetNativeFullScreenState();
        if (isFullScreen == _isFullScreen)
        {
            return;
        }

        _isFullScreen = isFullScreen;

        var message = new WindowFullScreenChangedMessage(isFullScreen);
        _messengerService.Send(message);
    }

    public Result EnterFullScreen()
    {
        if (GetNativeFullScreenState())
        {
            return Result.Ok();
        }

        ToggleNativeFullScreen();
        _logger.LogDebug("Entering native fullscreen");

        return Result.Ok();
    }

    public Result ExitFullScreen()
    {
        if (!GetNativeFullScreenState())
        {
            return Result.Ok();
        }

        ToggleNativeFullScreen();
        _logger.LogDebug("Exiting native fullscreen");

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

    private static void ToggleNativeFullScreen()
    {
#if !WINDOWS
        MacOSWindowInterop.ToggleMainWindowFullScreen();
#endif
    }
}
