using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Settings;
using Celbridge.UserInterface.Helpers.FullScreen;
using Celbridge.UserInterface.Views;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Helper class to manage window maximized state, bounds persistence, and fullscreen modes.
/// </summary>
public sealed class WindowStateHelper
{
    /// <summary>
    /// The smallest window the application is usable in, in device-independent pixels, and the budget the
    /// workspace layout floors are composed to fit inside. Authored rather than derived from the layout,
    /// because the application toolbar needs most of this width for its own content and composes no minimum
    /// of its own.
    /// </summary>
    public const int MinimumWindowWidth = 800;

    /// <summary>
    /// The smallest window height the application is usable in, in device-independent pixels.
    /// </summary>
    public const int MinimumWindowHeight = 600;

    /// <summary>
    /// The window frame either side of the application content. Counted against the minimum window size, which
    /// covers the whole window, while the composed minimum covers only the workspace inside it.
    /// </summary>
    public const int WindowFrameWidth = 16;

    /// <summary>
    /// The window frame below the application content, and the native title bar above it on the heads that draw
    /// one. Neither is a size the layout can measure, so the allowance is authored to cover the largest of them.
    /// </summary>
    public const int WindowFrameHeight = 40;

    private readonly ILogger<WindowStateHelper> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ISettingsService _settingsService;
    private readonly IFullScreenController _fullScreenController;
    private readonly IWindowBoundsValidator _windowBoundsValidator;
    private readonly IWindowSizeConstraints _windowSizeConstraints;
    private readonly IPlatformInfo _platformInfo;
    private Window? _mainWindow;
    private AppWindow? _appWindow;
    private OverlappedPresenter? _overlappedPresenter;
    private bool _isApplyingWindowMode;
    private bool _isTransitioningFromFullscreen;
    private AppWindowPresenterKind _previousPresenterKind;

    public WindowStateHelper(
        ILogger<WindowStateHelper> logger,
        IMessengerService messengerService,
        ISettingsService settingsService,
        IFullScreenController fullScreenController,
        IWindowBoundsValidator windowBoundsValidator,
        IWindowSizeConstraints windowSizeConstraints,
        IPlatformInfo platformInfo)
    {
        _logger = logger;
        _messengerService = messengerService;
        _settingsService = settingsService;
        _fullScreenController = fullScreenController;
        _windowBoundsValidator = windowBoundsValidator;
        _windowSizeConstraints = windowSizeConstraints;
        _platformInfo = platformInfo;
    }

    /// <summary>
    /// Initializes the helper with the main window and sets up state tracking.
    /// </summary>
    public Result Initialize(Window mainWindow)
    {
        _logger.LogDebug("Initializing WindowStateHelper");

        try
        {
            _mainWindow = mainWindow;

            // Window.AppWindow is the cross-platform Microsoft.UI.Windowing entry point and works on
            // both the packaged WinUI head and the Skia desktop head, so no Win32 interop is needed.
            _appWindow = mainWindow.AppWindow;
            if (_appWindow == null)
            {
                return Result.Fail("Failed to get AppWindow from main window");
            }

            _overlappedPresenter = _appWindow.Presenter as OverlappedPresenter;
            if (_overlappedPresenter == null)
            {
                return Result.Fail("AppWindow presenter is not an OverlappedPresenter");
            }

            // Track the initial presenter kind
            _previousPresenterKind = _appWindow.Presenter.Kind;

            // Bind the platform-specific fullscreen controller to this window.
            _fullScreenController.Initialize(_appWindow);

            ApplyMinimumWindowSize();

            // This is a "best-effort" restore. If it doesn't work, the default window state will be applied automatically.
            TryRestoreWindowState();

            if (_settingsService.Get(SettingCatalog.Window.IsMaximized))
            {
                _overlappedPresenter.Maximize();
            }

            // Track window state changes
            _appWindow.Changed += OnAppWindowChanged;

            // Listen for fullscreen state changes to drive the platform fullscreen controller
            _messengerService.Register<FullScreenChangedMessage>(this, OnFullScreenChanged);

            // Listen for requests to restore window state (e.g., after layout reset)
            _messengerService.Register<RestoreWindowStateMessage>(this, OnRestoreWindowState);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("Exception occurred during WindowStateHelper initialization")
                .WithException(ex);
        }
    }

    private void OnFullScreenChanged(object recipient, FullScreenChangedMessage message)
    {
        ApplyFullScreen(message.IsFullScreen);
    }

    private void OnRestoreWindowState(object recipient, RestoreWindowStateMessage message)
    {
        SyncWindowState();
    }

    /// <summary>
    /// Synchronizes the window's maximized/restored state with the current editor settings.
    /// </summary>
    private void SyncWindowState()
    {
        if (_overlappedPresenter == null || _appWindow?.Presenter.Kind != AppWindowPresenterKind.Overlapped)
        {
            return;
        }

        try
        {
            _isApplyingWindowMode = true;

            if (_settingsService.Get(SettingCatalog.Window.IsMaximized))
            {
                _overlappedPresenter.Maximize();
            }
            else
            {
                _overlappedPresenter.Restore();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync window state");
        }
        finally
        {
            _isApplyingWindowMode = false;
        }
    }

    /// <summary>
    /// Enters or exits fullscreen via the platform-specific controller.
    /// </summary>
    public void ApplyFullScreen(bool isFullScreen)
    {
        if (_appWindow == null)
        {
            return;
        }

        try
        {
            _isApplyingWindowMode = true;

            // The platform-specific controller owns the fullscreen mechanism and remembers the prior
            // windowed placement, so it restores maximized/bounds itself when leaving fullscreen.
            Result transitionResult;
            if (isFullScreen)
            {
                transitionResult = _fullScreenController.EnterFullScreen();
            }
            else
            {
                transitionResult = _fullScreenController.ExitFullScreen();
            }

            if (transitionResult.IsFailure)
            {
                _logger.LogError(transitionResult, $"Failed to apply fullscreen: {isFullScreen}");
            }

            // The controller may have re-created the presenter when switching presenter kinds, so
            // refresh the cached reference and the tracked kind for window-state tracking.
            _overlappedPresenter = _appWindow.Presenter as OverlappedPresenter;
            _previousPresenterKind = _appWindow.Presenter.Kind;

            // A replacement presenter does not carry over the size constraint.
            ApplyMinimumWindowSize();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to apply fullscreen: {isFullScreen}");
        }
        finally
        {
            _isApplyingWindowMode = false;
        }
    }

    private void ApplyMinimumWindowSize()
    {
        if (_appWindow == null)
        {
            return;
        }

        var minimumSize = ComposeMinimumWindowSize();

        _logger.LogDebug(
            "Applying minimum window size {Width} x {Height} at rasterization scale {Scale}, window sizes in physical pixels: {UsesPhysicalPixels}",
            minimumSize.Width,
            minimumSize.Height,
            _mainWindow?.Content?.XamlRoot?.RasterizationScale ?? 0,
            _platformInfo.WindowSizesUsePhysicalPixels);

        _windowSizeConstraints.ApplyMinimumSize(_appWindow, minimumSize);
    }

    // The authored minimum, held to at least the window the workspace needs: the composed floor of the layout a
    // workspace opens with, the application toolbar above it, and the window's own chrome around both. The
    // layout floors are chosen to fit inside the authored size, so the composed terms only take over if a floor
    // is ever raised past it, which grows the window rather than clipping the workspace. A layout the user has
    // widened or split needs more, and is clamped or clipped rather than holding the window open.
    private SizeInt32 ComposeMinimumWindowSize()
    {
        double gutterSize = (double)Application.Current.Resources["GutterSize"];
        var defaultVisibleSurfaces = SettingCatalog.Layout.PreferredSurfaceVisibility.DefaultValue;

        var workspaceMinimumSize = WorkspaceMinimumSize.ComposeDefaultLayout(defaultVisibleSurfaces, gutterSize);

        double workspaceWindowWidth = workspaceMinimumSize.Width + WindowFrameWidth;
        double workspaceWindowHeight = workspaceMinimumSize.Height +
            ApplicationToolbar.ToolbarHeight +
            WindowFrameHeight;

        double width = Math.Max(MinimumWindowWidth, workspaceWindowWidth);
        double height = Math.Max(MinimumWindowHeight, workspaceWindowHeight);
        double scale = WindowSizeScale;

        return new SizeInt32
        {
            Width = (int)Math.Ceiling(width * scale),
            Height = (int)Math.Ceiling(height * scale)
        };
    }

    // The composed minimum is in device-independent pixels, which is not what every head measures its window
    // in: the packaged Windows head uses physical pixels and its resize constraint does not scale them itself.
    private double WindowSizeScale
    {
        get
        {
            if (!_platformInfo.WindowSizesUsePhysicalPixels)
            {
                return 1;
            }

            double rasterizationScale = _mainWindow?.Content?.XamlRoot?.RasterizationScale ?? 1;
            if (rasterizationScale <= 0)
            {
                return 1;
            }

            return rasterizationScale;
        }
    }

    private void TryRestoreWindowState()
    {
        if (_appWindow == null)
        {
            return;
        }

        // Check if we should use saved window geometry
        if (!_settingsService.Get(SettingCatalog.Window.UsePreferredGeometry))
        {
            _logger.LogDebug("UsePreferredWindowGeometry is false, using default window placement");
            return;
        }

        int x = _settingsService.Get(SettingCatalog.Window.PreferredX);
        int y = _settingsService.Get(SettingCatalog.Window.PreferredY);
        int savedWidth = _settingsService.Get(SettingCatalog.Window.PreferredWidth);
        int savedHeight = _settingsService.Get(SettingCatalog.Window.PreferredHeight);

        // The platform constraint only applies to user resizing, and geometry saved when the minimum was a
        // different size may be smaller than it, so clamp the restored size here too.
        var minimumSize = ComposeMinimumWindowSize();
        int width = Math.Max(savedWidth, minimumSize.Width);
        int height = Math.Max(savedHeight, minimumSize.Height);

        // Object-initializer syntax is used for the Windows.Graphics structs because the Skia desktop
        // head's projection does not expose their positional constructors.
        var bounds = new RectInt32
        {
            X = x,
            Y = y,
            Width = width,
            Height = height
        };

        // Validate that the title bar area is visible on screen.
        if (!_windowBoundsValidator.IsTitleBarVisible(bounds))
        {
            _logger.LogDebug("Saved window position is off-screen, using default placement");
            return;
        }

        _appWindow.MoveAndResize(bounds);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // Ignore changes while we're in the middle of applying a window mode change, or while a
        // fullscreen mode is active, to avoid saving fullscreen dimensions as the preferred window
        // bounds. The desktop emulation keeps an overlapped presenter, so IsFullScreen is the only
        // reliable signal that the window is currently covering the screen.
        if (_isApplyingWindowMode ||
            _fullScreenController.IsFullScreen)
        {
            return;
        }

        // Detect when the user drags the window out of fullscreen mode
        // Windows automatically exits fullscreen when the user drags from the top of the screen
        if (args.DidPresenterChange)
        {
            var currentPresenterKind = sender.Presenter.Kind;
            
            // Only send the message if we're transitioning from FullScreen to Overlapped
            // This filters out normal windowed operations like maximize/restore
            if (_previousPresenterKind == AppWindowPresenterKind.FullScreen && 
                currentPresenterKind == AppWindowPresenterKind.Overlapped)
            {
                // Mark that we're transitioning from fullscreen
                // This prevents saving the fullscreen dimensions as preferred window bounds
                _isTransitioningFromFullscreen = true;
                
                // Notify the layout system that we've exited fullscreen via drag
                // This ensures the UI state is synchronized with the window state
                _messengerService.Send(new ExitedFullscreenViaDragMessage());
            }
            
            // Update the tracked presenter kind
            _previousPresenterKind = currentPresenterKind;
        }

        if (args.DidSizeChange ||
            args.DidPositionChange ||
            args.DidPresenterChange)
        {
            // Only track state when using overlapped presenter (windowed mode)
            if (sender.Presenter.Kind == AppWindowPresenterKind.Overlapped)
            {
                var presenter = sender.Presenter as OverlappedPresenter;
                if (presenter != null)
                {
                    bool isMaximized = presenter.State == OverlappedPresenterState.Maximized;
                    _settingsService.Set(SettingCatalog.Window.IsMaximized, isMaximized);

                    // Only save bounds when not maximized or minimized
                    // Also skip if we're transitioning from fullscreen to avoid saving fullscreen
                    // dimensions (happens when the user drags the window out of fullscreen)
                    if (presenter.State == OverlappedPresenterState.Restored && 
                        !_isTransitioningFromFullscreen)
                    {
                        SaveWindowBounds();
                    }
                    
                    // Clear the transition flag after the first restored state is processed
                    // The next size/position change will be the actual windowed dimensions
                    if (_isTransitioningFromFullscreen && presenter.State == OverlappedPresenterState.Restored)
                    {
                        _isTransitioningFromFullscreen = false;
                    }
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

        _settingsService.Set(SettingCatalog.Window.PreferredX, position.X);
        _settingsService.Set(SettingCatalog.Window.PreferredY, position.Y);
        _settingsService.Set(SettingCatalog.Window.PreferredWidth, size.Width);
        _settingsService.Set(SettingCatalog.Window.PreferredHeight, size.Height);

        // Mark that we now have valid saved geometry to restore on next startup
        _settingsService.Set(SettingCatalog.Window.UsePreferredGeometry, true);
    }
}
