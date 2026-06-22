using Celbridge.Logging;
using Microsoft.UI.Windowing;

namespace Celbridge.UserInterface.Helpers.FullScreen;

/// <summary>
/// Fullscreen controller for the packaged WinAppSDK head, where the native fullscreen presenter works
/// as documented. Entering switches to the fullscreen presenter; exiting returns to the overlapped
/// presenter and restores the prior maximized state.
/// </summary>
public sealed class WinAppSdkFullScreenController : IFullScreenController
{
    private readonly ILogger<WinAppSdkFullScreenController> _logger;
    private AppWindow? _appWindow;
    private bool _wasMaximized;

    public WinAppSdkFullScreenController(ILogger<WinAppSdkFullScreenController> logger)
    {
        _logger = logger;
    }

    public bool IsFullScreen => _appWindow?.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    public void Initialize(AppWindow appWindow)
    {
        _appWindow = appWindow;
    }

    public Result EnterFullScreen()
    {
        if (_appWindow is null)
        {
            return Result.Fail("Cannot enter fullscreen before the controller is initialized");
        }

        if (_appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            return Result.Ok();
        }

        // Remember the windowed maximized state so it can be restored when leaving fullscreen.
        var overlappedPresenter = _appWindow.Presenter as OverlappedPresenter;
        _wasMaximized = overlappedPresenter?.State == OverlappedPresenterState.Maximized;

        _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        _logger.LogDebug($"Entered fullscreen presenter (restoring to maximized: {_wasMaximized})");

        return Result.Ok();
    }

    public Result ExitFullScreen()
    {
        if (_appWindow is null)
        {
            return Result.Fail("Cannot exit fullscreen before the controller is initialized");
        }

        if (_appWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped)
        {
            return Result.Ok();
        }

        _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);

        if (_wasMaximized &&
            _appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        _logger.LogDebug("Exited fullscreen presenter");

        return Result.Ok();
    }
}
