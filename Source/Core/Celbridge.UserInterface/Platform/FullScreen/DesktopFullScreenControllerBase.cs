using Celbridge.Logging;
using Celbridge.UserInterface.Helpers.FullScreen;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Celbridge.UserInterface.Platform.FullScreen;

/// <summary>
/// Shared fullscreen emulation for the Skia desktop heads. The WPF shell's native fullscreen
/// presenter has no visual effect and the DisplayArea APIs that would report the monitor bounds are
/// not implemented, so fullscreen is emulated by removing the window border (cross-platform) and
/// covering the monitor through a platform-specific hook. Subclasses provide that hook for their head.
/// </summary>
public abstract class DesktopFullScreenControllerBase : IFullScreenController
{
    private readonly ILogger _logger;
    private AppWindow? _appWindow;
    private bool _isFullScreen;
    private bool _wasMaximized;
    private RectInt32 _restoreBounds;

    protected DesktopFullScreenControllerBase(ILogger logger)
    {
        _logger = logger;
    }

    protected ILogger Logger => _logger;

    public bool IsFullScreen => _isFullScreen;

    public void Initialize(AppWindow appWindow)
    {
        _appWindow = appWindow;
    }

    public virtual Result EnterFullScreen()
    {
        if (_appWindow is null)
        {
            return Result.Fail("Cannot enter fullscreen before the controller is initialized");
        }

        if (_isFullScreen)
        {
            return Result.Ok();
        }

        if (_appWindow.Presenter is not OverlappedPresenter presenter)
        {
            return Result.Fail("Cannot enter fullscreen: the window is not using an overlapped presenter");
        }

        // Remember the windowed placement so ExitFullScreen can restore it exactly. Object-initializer
        // syntax is used because the Skia desktop head's projection does not expose the positional
        // constructor for RectInt32.
        _wasMaximized = presenter.State == OverlappedPresenterState.Maximized;
        _restoreBounds = new RectInt32
        {
            X = _appWindow.Position.X,
            Y = _appWindow.Position.Y,
            Width = _appWindow.Size.Width,
            Height = _appWindow.Size.Height
        };

        // A maximized window ignores explicit positioning, so return to the normal state first.
        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            presenter.Restore();
        }

        TrySetBorderAndTitleBar(presenter, isVisible: false);

        // Covering the monitor (including the taskbar) is platform-specific because the cross-platform
        // DisplayArea APIs are not implemented on the desktop head. Fall back to a borderless maximize
        // (work area only) when the platform cannot determine the monitor bounds.
        if (!TryCoverMonitor())
        {
            presenter.Maximize();
        }

        _isFullScreen = true;
        _logger.LogDebug($"Entered emulated fullscreen (restoring to maximized: {_wasMaximized})");

        return Result.Ok();
    }

    public virtual Result ExitFullScreen()
    {
        if (_appWindow is null)
        {
            return Result.Fail("Cannot exit fullscreen before the controller is initialized");
        }

        if (!_isFullScreen)
        {
            return Result.Ok();
        }

        if (_appWindow.Presenter is not OverlappedPresenter presenter)
        {
            return Result.Fail("Cannot exit fullscreen: the window is not using an overlapped presenter");
        }

        ReleaseMonitorCover();
        TrySetBorderAndTitleBar(presenter, isVisible: true);

        // Clear the flag before restoring placement so window-state tracking resumes for the restored size.
        _isFullScreen = false;

        if (_wasMaximized)
        {
            presenter.Maximize();
        }
        else
        {
            presenter.Restore();
            _appWindow.MoveAndResize(_restoreBounds);
        }

        _logger.LogDebug("Exited emulated fullscreen");

        return Result.Ok();
    }

    /// <summary>
    /// Positions the window to cover the whole monitor, including the taskbar, and pins it above other
    /// windows. Returns false if the monitor bounds could not be determined on this platform.
    /// </summary>
    protected abstract bool TryCoverMonitor();

    /// <summary>
    /// Releases the taskbar-covering state applied by TryCoverMonitor.
    /// </summary>
    protected abstract void ReleaseMonitorCover();

    private void TrySetBorderAndTitleBar(OverlappedPresenter presenter, bool isVisible)
    {
        try
        {
            presenter.SetBorderAndTitleBar(hasBorder: isVisible, hasTitleBar: isVisible);
        }
        catch (NotImplementedException)
        {
            _logger.LogDebug("OverlappedPresenter.SetBorderAndTitleBar is not implemented on this head");
        }
    }
}
