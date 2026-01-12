using Celbridge.Logging;
using Celbridge.Settings;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Centralized manager for window modes and panel visibility state.
/// Implements a state machine with clear transitions between allowed states.
/// </summary>
public class LayoutManager : ILayoutManager
{
    private const float DefaultContextPanelWidth = 300f;
    private const float DefaultInspectorPanelWidth = 300f;
    private const float DefaultConsolePanelHeight = 350f;

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

    public bool IsFullScreen => WindowMode != WindowMode.Windowed;

    public bool IsContextPanelVisible => PanelVisibility.HasFlag(PanelVisibilityFlags.Context);

    public bool IsInspectorPanelVisible => PanelVisibility.HasFlag(PanelVisibilityFlags.Inspector);

    public bool IsConsolePanelVisible => PanelVisibility.HasFlag(PanelVisibilityFlags.Console);

    public Result RequestTransition(LayoutTransition transition)
    {
        _logger.LogDebug($"Requesting layout transition: {transition} (current mode: {WindowMode})");

        switch (transition)
        {
            case LayoutTransition.EnterWindowed:
                return TransitionToWindowed();

            case LayoutTransition.EnterFullScreen:
                return TransitionToFullScreen();

            case LayoutTransition.EnterZenMode:
                return TransitionToZenMode();

            case LayoutTransition.EnterPresenterMode:
                return TransitionToPresenterMode();

            case LayoutTransition.ToggleLayout:
                return HandleToggleLayout();

            case LayoutTransition.ResetLayout:
                return HandleResetLayout();

            default:
                return Result.Fail($"Unknown layout transition: {transition}");
        }
    }

    public void SetPanelVisibility(PanelVisibilityFlags panel, bool isVisible)
    {
        var newVisibility = isVisible
            ? PanelVisibility | panel
            : PanelVisibility & ~panel;

        if (newVisibility == PanelVisibility)
        {
            return;
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

    private Result HandleToggleLayout()
    {
        if (WindowMode == WindowMode.Windowed)
        {
            // Enter fullscreen
            // We actually use ZenMode rather than FullScreen as this provides a better user experience.
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
        // Reset panel sizes
        _editorSettings.ContextPanelWidth = DefaultContextPanelWidth;
        _editorSettings.InspectorPanelWidth = DefaultInspectorPanelWidth;
        _editorSettings.ConsolePanelHeight = DefaultConsolePanelHeight;

        // Reset preferred visibility to all panels
        _editorSettings.PreferredPanelVisibility = PanelVisibilityFlags.All;

        // Show all panels. This change should persist.
        UpdatePanelVisibility(PanelVisibilityFlags.All, shouldPersist: true);

        // Return to Windowed mode if in fullscreen
        if (WindowMode != WindowMode.Windowed)
        {
            SetWindowModeInternal(WindowMode.Windowed);
        }

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
