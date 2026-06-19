using Celbridge.Logging;
using Celbridge.Settings;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Centralized manager for window modes and panel visibility state.
/// Implements a state machine with clear transitions between allowed states.
/// </summary>
public class LayoutManager : IWindowModeService, ILayoutService
{
    private readonly ILogger<LayoutManager> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ISettingsService _settingsService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IFeatureFlags _featureFlags;

    private WindowMode _windowMode = WindowMode.Windowed;
    private LayoutRegion _regionVisibility = LayoutRegion.All;

    public LayoutManager(
        ILogger<LayoutManager> logger,
        IMessengerService messengerService,
        ISettingsService settingsService,
        IWorkspaceWrapper workspaceWrapper,
        IFeatureFlags featureFlags)
    {
        _logger = logger;
        _messengerService = messengerService;
        _settingsService = settingsService;
        _workspaceWrapper = workspaceWrapper;
        _featureFlags = featureFlags;

        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);

        // Listen for when the user exits fullscreen by dragging the window (Windows built-in behavior)
        _messengerService.Register<ExitedFullscreenViaDragMessage>(this, OnExitedFullscreenViaDrag);
        _messengerService.Register<FeatureFlagsChangedMessage>(this, OnFeatureFlagsChanged);
    }

    // The typed workspace settings facade, or null when no workspace is loaded.
    // Panel layout is Workspace-scoped, so it has no meaning outside a project.
    private IBindableWorkspaceSettings? WorkspaceSettings =>
        _workspaceWrapper.IsWorkspacePageLoaded
            ? _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings
            : null;

    // The project's preferred region visibility, falling back to all regions
    // when no workspace is loaded.
    private LayoutRegion PreferredRegionVisibility =>
        WorkspaceSettings?.PreferredRegionVisibility ?? LayoutRegion.All;

    // Persists the preferred region visibility for the current project. A no-op
    // when no workspace is loaded.
    private void PersistPreferredRegionVisibility(LayoutRegion visibility)
    {
        var workspaceSettings = WorkspaceSettings;
        if (workspaceSettings is not null)
        {
            workspaceSettings.PreferredRegionVisibility = visibility;
        }
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        // The workspace settings are now loaded, so apply this project's preferred
        // region visibility. No need to persist, we are restoring the saved state.
        UpdateRegionVisibility(PreferredRegionVisibility, shouldPersist: false);
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

    public LayoutRegion RegionVisibility
    {
        get => _regionVisibility;
        private set
        {
            if (_regionVisibility != value)
            {
                _regionVisibility = value;
            }
        }
    }

    public bool IsContextPanelVisible => RegionVisibility.HasFlag(LayoutRegion.Primary);

    public bool IsInspectorPanelVisible => RegionVisibility.HasFlag(LayoutRegion.Secondary);

    public bool IsConsolePanelVisible => RegionVisibility.HasFlag(LayoutRegion.Console);

    public void SetRegionVisibility(LayoutRegion region, bool isVisible)
    {
        var newVisibility = isVisible
            ? RegionVisibility | region
            : RegionVisibility & ~region;

        if (newVisibility == RegionVisibility)
        {
            return;
        }

        // If hiding console while maximized, restore first
        if (!isVisible &&
            region.HasFlag(LayoutRegion.Console) &&
            IsConsoleMaximized)
        {
            SetConsoleMaximized(false);
        }

        // This is a user-initiated change, so it should persist
        UpdateRegionVisibility(newVisibility, shouldPersist: true);

        // Sync window mode to match the new state
        UpdateWindowMode();
    }

    public void ToggleRegionVisibility(LayoutRegion region)
    {
        var isCurrentlyVisible = RegionVisibility.HasFlag(region);
        SetRegionVisibility(region, !isCurrentlyVisible);
    }

    public bool IsConsoleMaximized => WorkspaceSettings?.IsConsoleMaximized ?? false;

    public void SetConsoleMaximized(bool isMaximized)
    {
        if (IsConsoleMaximized == isMaximized)
        {
            return;
        }

        // Cannot maximize console if it's not visible
        if (isMaximized && !IsConsolePanelVisible)
        {
            return;
        }

        var workspaceSettings = WorkspaceSettings;
        if (workspaceSettings is null)
        {
            return;
        }

        workspaceSettings.IsConsoleMaximized = isMaximized;

        // Broadcast the change
        var message = new ConsoleMaximizedChangedMessage(isMaximized);
        _messengerService.Send(message);

        _logger.LogDebug($"Console maximized state changed: {isMaximized}");

        // Sync window mode to match the new state
        UpdateWindowMode();
    }

    /// <summary>
    /// Evaluates the current panel visibility and console state to determine
    /// the appropriate window mode, then transitions if necessary.
    /// </summary>
    private void UpdateWindowMode()
    {
        // Only sync between ZenMode and FullScreen - other modes are explicit user choices
        if (WindowMode != WindowMode.ZenMode &&
            WindowMode != WindowMode.FullScreen)
        {
            return;
        }

        var screenMode = EvaluateFullscreenMode();
        if (WindowMode != screenMode)
        {
            SetWindowModeInternal(screenMode);
        }
    }

    /// <summary>
    /// Determines whether the current state matches ZenMode or FullScreen criteria.
    /// ZenMode: No sidebars visible AND (no panels OR only maximized console)
    /// FullScreen: Any other fullscreen configuration
    /// </summary>
    private WindowMode EvaluateFullscreenMode()
    {
        var sidebarRegions = LayoutRegion.Primary | LayoutRegion.Secondary;
        var sidebarsHidden = (RegionVisibility & sidebarRegions) == LayoutRegion.None;

        // Zen Mode requires:
        // 1. No sidebar regions visible, AND
        // 2. Either no regions at all, OR only console visible AND maximized
        var isZenModeState = sidebarsHidden &&
            (RegionVisibility == LayoutRegion.None ||
             (RegionVisibility == LayoutRegion.Console && IsConsoleMaximized));

        return isZenModeState ? WindowMode.ZenMode : WindowMode.FullScreen;
    }

    private void OnFeatureFlagsChanged(object recipient, FeatureFlagsChangedMessage message)
    {
        // Re-evaluate console visibility based on updated feature flags
        var isConsolePanelEnabled = _featureFlags.IsEnabled(FeatureFlagConstants.ConsolePanel);
        if (!isConsolePanelEnabled &&
            RegionVisibility.HasFlag(LayoutRegion.Console))
        {
            UpdateRegionVisibility(RegionVisibility & ~LayoutRegion.Console, shouldPersist: true);
        }
    }

    private void OnExitedFullscreenViaDrag(object recipient, ExitedFullscreenViaDragMessage message)
    {
        // The window has exited fullscreen via drag, so sync our internal state to Windowed mode
        // This ensures the UI state matches the actual window state
        if (WindowMode != WindowMode.Windowed)
        {
            _logger.LogDebug("Detected fullscreen exit via drag, transitioning to Windowed mode");

            // Restore the preferred panel visibility configuration
            UpdateRegionVisibility(PreferredRegionVisibility, shouldPersist: false);

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
        UpdateRegionVisibility(PreferredRegionVisibility, shouldPersist: false);
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
        UpdateRegionVisibility(PreferredRegionVisibility, shouldPersist: false);
        SetWindowModeInternal(WindowMode.FullScreen);

        return Result.Ok();
    }

    private Result TransitionToZenMode()
    {
        if (WindowMode == WindowMode.ZenMode)
        {
            return Result.Ok(); // Already in ZenMode
        }

        // If console is maximized, keep it visible in Zen Mode (fullscreen console).
        // This allows the user to continue working in the console with full screen space.
        // Otherwise, hide all panels for fullscreen documents.
        var zenModeVisibility = IsConsoleMaximized
            ? LayoutRegion.Console
            : LayoutRegion.None;

        // Don't persist this change as it's only temporary.
        UpdateRegionVisibility(zenModeVisibility, shouldPersist: false);
        SetWindowModeInternal(WindowMode.ZenMode);

        return Result.Ok();
    }

    private Result TransitionToPresenterMode()
    {
        if (WindowMode == WindowMode.Presenter)
        {
            return Result.Ok(); // Already in Presenter mode
        }

        // If console is maximized, keep it visible in Presenter Mode (fullscreen console).
        // This allows the user to present console output with full screen space.
        // Otherwise, hide all panels for fullscreen document presentation.
        var presenterModeVisibility = IsConsoleMaximized
            ? LayoutRegion.Console
            : LayoutRegion.None;

        // Don't persist this change as it's only temporary.
        UpdateRegionVisibility(presenterModeVisibility, shouldPersist: false);
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
        var workspaceSettings = WorkspaceSettings;
        if (workspaceSettings is not null)
        {
            workspaceSettings.PrimaryPanelWidth = WorkspaceConstants.PrimaryPanelWidth;
            workspaceSettings.SecondaryPanelWidth = WorkspaceConstants.SecondaryPanelWidth;
            workspaceSettings.ConsolePanelHeight = WorkspaceConstants.ConsolePanelHeight;
        }

        // Reset preferred window geometry
        _settingsService.Set(SettingCatalog.Window.UsePreferredGeometry, false);
        _settingsService.Set(SettingCatalog.Window.PreferredX, 0);
        _settingsService.Set(SettingCatalog.Window.PreferredY, 0);
        _settingsService.Set(SettingCatalog.Window.PreferredWidth, 0);
        _settingsService.Set(SettingCatalog.Window.PreferredHeight, 0);
        _settingsService.Set(SettingCatalog.Window.IsMaximized, false);

        // Reset preferred visibility to all regions, but exclude Console if feature is disabled
        var isConsolePanelEnabled = _featureFlags.IsEnabled(FeatureFlagConstants.ConsolePanel);
        var targetVisibility = isConsolePanelEnabled 
            ? LayoutRegion.All 
            : (LayoutRegion.Primary | LayoutRegion.Secondary);

        UpdateRegionVisibility(targetVisibility, shouldPersist: true);
        PersistPreferredRegionVisibility(targetVisibility);

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

    private void UpdateRegionVisibility(LayoutRegion newVisibility, bool shouldPersist)
    {
        if (RegionVisibility == newVisibility)
        {
            return;
        }

        var oldVisibility = RegionVisibility;
        RegionVisibility = newVisibility;

        // Only persist if explicitly requested (user-initiated changes)
        // and not in Presenter mode (temporary presentation state)
        if (shouldPersist && WindowMode != WindowMode.Presenter)
        {
            PersistPreferredRegionVisibility(newVisibility);
        }

        // Broadcast the change
        var message = new RegionVisibilityChangedMessage(newVisibility);
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
