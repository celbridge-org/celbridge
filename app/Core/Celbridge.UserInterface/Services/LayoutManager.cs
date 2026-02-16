using Celbridge.Logging;
using Celbridge.Settings;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Centralized manager for window modes and panel visibility state.
/// Implements a state machine with clear transitions between allowed states.
/// </summary>
public class LayoutManager : ILayoutManager
{
    private readonly ILogger<LayoutManager> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IEditorSettings _editorSettings;

    private WindowMode _windowMode = WindowMode.Windowed;
    private PanelVisibilityFlags _panelVisibility = PanelVisibilityFlags.All;

    public LayoutManager(
        ILogger<LayoutManager> logger,
        IMessengerService messengerService,
        IEditorSettings editorSettings)
    {
        _logger = logger;
        _messengerService = messengerService;
        _editorSettings = editorSettings;

        // Initialize from persisted preferences
        _panelVisibility = _editorSettings.PreferredPanelVisibility;

        // Listen for when the user exits fullscreen by dragging the window (Windows built-in behavior)
        _messengerService.Register<ExitedFullscreenViaDragMessage>(this, OnExitedFullscreenViaDrag);
    }

    public WindowMode WindowMode
    {
        get => _windowMode;
        private set
        {
            if (_windowMode != value)
            {
                _windowMode = value;
            }
        }
    }

    public Result RequestWindowModeTransition(WindowModeTransition transition)
    {
        _logger.LogDebug($"Requesting layout transition: {transition} (current mode: {WindowMode})");

        switch (transition)
        {
            case WindowModeTransition.EnterWindowed:
                return TransitionToWindowed();

            case WindowModeTransition.EnterFullScreen:
                return TransitionToFullScreen();

            case WindowModeTransition.EnterZenMode:
                return TransitionToZenMode();

            case WindowModeTransition.EnterPresenterMode:
                return TransitionToPresenterMode();

            case WindowModeTransition.ToggleZenMode:
                return HandleToggleZenMode();

            case WindowModeTransition.ResetLayout:
                return HandleResetLayout();

            default:
                return Result.Fail($"Unknown layout transition: {transition}");
        }
    }

    public bool IsFullScreen => WindowMode != WindowMode.Windowed;

    public PanelVisibilityFlags PanelVisibility
    {
        get => _panelVisibility;
        private set
        {
            if (_panelVisibility != value)
            {
                _panelVisibility = value;
            }
        }
    }

    public bool IsContextPanelVisible => PanelVisibility.HasFlag(PanelVisibilityFlags.Primary);

    public bool IsInspectorPanelVisible => PanelVisibility.HasFlag(PanelVisibilityFlags.Secondary);

    public bool IsConsolePanelVisible => PanelVisibility.HasFlag(PanelVisibilityFlags.Console);

    public void SetPanelVisibility(PanelVisibilityFlags panel, bool isVisible)
    {
        var newVisibility = isVisible
            ? PanelVisibility | panel
            : PanelVisibility & ~panel;

        if (newVisibility == PanelVisibility)
        {
            return;
        }

        // If hiding console while maximized, restore first
        if (!isVisible && panel.HasFlag(PanelVisibilityFlags.Console) && IsConsoleMaximized)
        {
            SetConsoleMaximized(false);
        }

        // This is a user-initiated change, so it should should persist
        UpdatePanelVisibility(newVisibility, shouldPersist: true);

        // Handle mode transitions based on panel visibility changes
        if (WindowMode == WindowMode.ZenMode && newVisibility != PanelVisibilityFlags.None)
        {
            // User showed a panel while in ZenMode, transition to FullScreen
            SetWindowModeInternal(WindowMode.FullScreen);
        }
        else if (WindowMode == WindowMode.FullScreen && newVisibility == PanelVisibilityFlags.None)
        {
            // User hid all panels while in FullScreen, transition to ZenMode
            SetWindowModeInternal(WindowMode.ZenMode);
        }
    }

    public void TogglePanelVisibility(PanelVisibilityFlags panel)
    {
        var isCurrentlyVisible = PanelVisibility.HasFlag(panel);
        SetPanelVisibility(panel, !isCurrentlyVisible);
    }

    public bool IsConsoleMaximized => _editorSettings.IsConsoleMaximized;

    public void SetConsoleMaximized(bool isMaximized)
    {
        if (_editorSettings.IsConsoleMaximized == isMaximized)
        {
            return;
        }

        // Cannot maximize console if it's not visible
        if (isMaximized && !IsConsolePanelVisible)
        {
            return;
        }

        // Cannot maximize console in ZenMode (console is hidden)
        if (isMaximized && WindowMode == WindowMode.ZenMode)
        {
            return;
        }

        _editorSettings.IsConsoleMaximized = isMaximized;

        // Broadcast the change
        var message = new ConsoleMaximizedChangedMessage(isMaximized);
        _messengerService.Send(message);

        _logger.LogDebug($"Console maximized state changed: {isMaximized}");
    }

    private void OnExitedFullscreenViaDrag(object recipient, ExitedFullscreenViaDragMessage message)
    {
        // The window has exited fullscreen via drag, so sync our internal state to Windowed mode
        // This ensures the UI state matches the actual window state
        if (WindowMode != WindowMode.Windowed)
        {
            _logger.LogDebug("Detected fullscreen exit via drag, transitioning to Windowed mode");

            // Restore the preferred panel visibility configuration
            UpdatePanelVisibility(_editorSettings.PreferredPanelVisibility, shouldPersist: false);

            // Update internal state without sending another WindowModeChangedMessage
            // since the window is already in the correct state
            WindowMode = WindowMode.Windowed;

            // Still need to send the message so other UI components update
            var windowModeMessage = new WindowModeChangedMessage(WindowMode.Windowed);
            _messengerService.Send(windowModeMessage);
        }
    }

    private Result TransitionToWindowed()
    {
        if (WindowMode == WindowMode.Windowed)
        {
            return Result.Ok(); // Already in Windowed mode
        }

        // Restore the preferred panel visibility configuration.
        // No need to persist this change, we're just restoring the saved state.
        UpdatePanelVisibility(_editorSettings.PreferredPanelVisibility, shouldPersist: false);
        SetWindowModeInternal(WindowMode.Windowed);

        return Result.Ok();
    }

    private Result TransitionToFullScreen()
    {
        if (WindowMode == WindowMode.FullScreen)
        {
            return Result.Ok(); // Already in FullScreen mode
        }

        // Restore the preferred panel visibility configuration.
        // No need to persist this change, we're just restoring the saved state.
        UpdatePanelVisibility(_editorSettings.PreferredPanelVisibility, shouldPersist: false);
        SetWindowModeInternal(WindowMode.FullScreen);

        return Result.Ok();
    }

    private Result TransitionToZenMode()
    {
        if (WindowMode == WindowMode.ZenMode)
        {
            return Result.Ok(); // Already in ZenMode
        }

        // Restore console if maximized before entering ZenMode
        if (IsConsoleMaximized)
        {
            SetConsoleMaximized(false);
        }

        // Hide all panels temporarily
        // Don't persist this change as it's only temporary.
        UpdatePanelVisibility(PanelVisibilityFlags.None, shouldPersist: false);
        SetWindowModeInternal(WindowMode.ZenMode);

        return Result.Ok();
    }

    private Result TransitionToPresenterMode()
    {
        if (WindowMode == WindowMode.Presenter)
        {
            return Result.Ok(); // Already in Presenter mode
        }

        // Hide all panels temporarily
        // Don't persist this change as it's only temporary.
        UpdatePanelVisibility(PanelVisibilityFlags.None, shouldPersist: false);
        SetWindowModeInternal(WindowMode.Presenter);

        return Result.Ok();
    }

    private Result HandleToggleZenMode()
    {
        if (WindowMode == WindowMode.Windowed)
        {
            // Enter Zen Mode (fullscreen with all panels hidden)
            return TransitionToZenMode();
        }
        else
        {
            // Exit any fullscreen mode back to Windowed
            return TransitionToWindowed();
        }
    }

    private Result HandleResetLayout()
    {
        // Reset console maximized state
        if (IsConsoleMaximized)
        {
            SetConsoleMaximized(false);
        }

        // Reset panel sizes
        _editorSettings.PrimaryPanelWidth = UserInterfaceConstants.PrimaryPanelWidth;
        _editorSettings.SecondaryPanelWidth = UserInterfaceConstants.SecondaryPanelWidth;
        _editorSettings.ConsolePanelHeight = UserInterfaceConstants.ConsolePanelHeight;
        _editorSettings.RestoreConsoleHeight = UserInterfaceConstants.ConsolePanelHeight;

        // Reset preferred window geometry
        _editorSettings.UsePreferredWindowGeometry = false;
        _editorSettings.PreferredWindowX = 0;
        _editorSettings.PreferredWindowY = 0;
        _editorSettings.PreferredWindowWidth = 0;
        _editorSettings.PreferredWindowHeight = 0;
        _editorSettings.IsWindowMaximized = false;

        // Reset preferred visibility to all panels
        // Doing this both ways to be double sure
        UpdatePanelVisibility(PanelVisibilityFlags.All, shouldPersist: true);
        _editorSettings.PreferredPanelVisibility = PanelVisibilityFlags.All;

        // Return to Windowed mode if in fullscreen
        if (WindowMode != WindowMode.Windowed)
        {
            SetWindowModeInternal(WindowMode.Windowed);
        }
        else
        {
            // Already in Windowed mode, but need to sync window state (e.g., restore from maximized)
            _messengerService.Send(new RestoreWindowStateMessage());
        }

        // Notify listeners to reset their layout state (e.g., document sections)
        var message = new ResetLayoutRequestedMessage();
        _messengerService.Send(message);

        return Result.Ok();
    }

    private void UpdatePanelVisibility(PanelVisibilityFlags newVisibility, bool shouldPersist)
    {
        if (PanelVisibility == newVisibility)
        {
            return;
        }

        var oldVisibility = PanelVisibility;
        PanelVisibility = newVisibility;

        // Only persist if explicitly requested (user-initiated changes)
        // and not in Presenter mode (temporary presentation state)
        if (shouldPersist && WindowMode != WindowMode.Presenter)
        {
            _editorSettings.PreferredPanelVisibility = newVisibility;
        }

        // Broadcast the change
        var message = new PanelVisibilityChangedMessage(newVisibility);
        _messengerService.Send(message);

        _logger.LogDebug($"Panel visibility changed: {oldVisibility} -> {newVisibility} (persist: {shouldPersist})");
    }

    private void SetWindowModeInternal(WindowMode newMode)
    {
        if (WindowMode == newMode)
        {
            return;
        }

        var oldMode = WindowMode;
        WindowMode = newMode;

        // Broadcast the change
        var message = new WindowModeChangedMessage(newMode);
        _messengerService.Send(message);

        _logger.LogDebug($"Window mode changed: {oldMode} -> {newMode}");
    }
}
