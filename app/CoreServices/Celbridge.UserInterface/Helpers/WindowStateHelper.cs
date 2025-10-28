#if WINDOWS

using Celbridge.Logging;
using Celbridge.Settings;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Helper class to manage window maximized state and bounds persistence.
/// </summary>
public sealed class WindowStateHelper
{
    private readonly ILogger<WindowStateHelper> _logger;
    private readonly IEditorSettings _editorSettings;
    private AppWindow? _appWindow;

    public WindowStateHelper(
        ILogger<WindowStateHelper> logger, 
        IEditorSettings editorSettings)
    {
        _logger = logger;
        _editorSettings = editorSettings;
    }

    /// <summary>
    /// Initializes the helper with the main window and sets up state tracking.
    /// </summary>
    public Result Initialize(Window mainWindow)
    {
        _logger.LogDebug("Initializing WindowStateHelper");

        try
        {
            _appWindow = GetAppWindow(mainWindow);
            if (_appWindow == null)
            {
                return Result.Fail("Failed to get AppWindow from main window");
            }

            var presenter = _appWindow.Presenter as OverlappedPresenter;
            if (presenter == null)
            {
                return Result.Fail("AppWindow presenter is not an OverlappedPresenter");
            }

            // This is a "best-effort" restore. If it doesn't work, the default window state will be applied automatically.
            TryRestoreWindowState();

            if (_editorSettings.IsWindowMaximized)
            {
                presenter.Maximize();
            }

            // Track window state changes
            _appWindow.Changed += OnAppWindowChanged;

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("Exception occurred during WindowStateHelper initialization")
                .WithException(ex);
        }
    }

    private void TryRestoreWindowState()
    {
        if (_appWindow == null)
        {
            return;
        }

        // Check if we have saved window bounds (-1 indicates no saved position)
        if (_editorSettings.WindowX < 0 || _editorSettings.WindowY < 0)
        {
            _logger.LogDebug("No saved window position found, using default");
            return;
        }

        int x = _editorSettings.WindowX;
        int y = _editorSettings.WindowY;
        int width = _editorSettings.WindowWidth;
        int height = _editorSettings.WindowHeight;

        // Validate that the title bar area is visible on screen
        if (!IsTitleBarVisible(x, y, width, height))
        {
            _logger.LogDebug("Saved window position is off-screen, using default placement");
            return;
        }

        // Apply the saved bounds
        var position = new PointInt32(x, y);
        var size = new SizeInt32(width, height);

        _appWindow.MoveAndResize(new RectInt32(position.X, position.Y, size.Width, size.Height));
    }

    private bool IsTitleBarVisible(int x, int y, int width, int height)
    {
        try
        {
            // Check if any part of the title bar area (top ~40 pixels of window) is visible on any display
            const int titleBarHeight = 40;
            var titleBarRect = new RectInt32(x, y, width, titleBarHeight);

            var displayAreas = DisplayArea.FindAll();
            if (displayAreas == null || displayAreas.Count == 0)
            {
                return false;
            }

            // Using foreach here causes an exception, using the workaround described here:
            // https://github.com/microsoft/microsoft-ui-xaml/issues/6454#issuecomment-2188377618
            for (int i = 0; i < displayAreas.Count; i++)
            {
                var displayArea = displayAreas[i];
                if (displayArea == null)
                {
                    continue;
                }

                var workArea = displayArea.WorkArea;

                // Check if the title bar intersects with this display's work area
                if (RectanglesIntersect(titleBarRect, workArea))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception occurred while checking display areas");
            return false;
        }
    }

    private bool RectanglesIntersect(RectInt32 rect1, RectInt32 rect2)
    {
        return rect1.X < rect2.X + rect2.Width &&
               rect1.X + rect1.Width > rect2.X &&
               rect1.Y < rect2.Y + rect2.Height &&
               rect1.Y + rect1.Height > rect2.Y;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange || 
            args.DidPositionChange || 
            args.DidPresenterChange)
        {
            var presenter = sender.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                bool isMaximized = presenter.State == OverlappedPresenterState.Maximized;
                _editorSettings.IsWindowMaximized = isMaximized;

                // Only save bounds when not maximized or minimized
                if (presenter.State == OverlappedPresenterState.Restored)
                {
                    SaveWindowBounds();
                }
            }
        }
    }

    private void SaveWindowBounds()
    {
        if (_appWindow == null)
        {
            return;
        }

        var position = _appWindow.Position;
        var size = _appWindow.Size;

        _editorSettings.WindowX = position.X;
        _editorSettings.WindowY = position.Y;
        _editorSettings.WindowWidth = size.Width;
        _editorSettings.WindowHeight = size.Height;
    }

    private static AppWindow? GetAppWindow(Window? window)
    {
        if (window == null) return null;

        var windowHandle = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        return AppWindow.GetFromWindowId(windowId);
    }
}
#endif
