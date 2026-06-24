using Celbridge.Logging;
using Celbridge.Settings;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Centralized manager for the window layout mode (chrome level) and panel visibility. The layout mode
/// and the fullscreen state are independent: changing one does not change the other.
/// </summary>
public class LayoutManager : IWindowModeService, ILayoutService
{
    private readonly ILogger<LayoutManager> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ISettingsService _settingsService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IFeatureFlags _featureFlags;

    private LayoutMode _layoutMode = LayoutMode.Default;
    private bool _isFullScreen;
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

    public LayoutMode LayoutMode => _layoutMode;

    public bool IsFullScreen => _isFullScreen;

    public Result RequestLayoutTransition(LayoutTransition transition)
    {
        _logger.LogDebug($"Requesting layout transition: {transition} (current mode: {_layoutMode}, fullscreen: {_isFullScreen})");

        switch (transition)
        {
            case LayoutTransition.Default:
                return TransitionToLayoutMode(LayoutMode.Default);

            case LayoutTransition.Focus:
                return TransitionToLayoutMode(LayoutMode.Focus);

            case LayoutTransition.Presentation:
                return TransitionToLayoutMode(LayoutMode.Presentation);

            case LayoutTransition.ToggleFocus:
                return HandleToggleFocus();

            case LayoutTransition.ToggleFullScreen:
                return HandleToggleFullScreen();

            case LayoutTransition.ResetLayout:
                return HandleResetLayout();

            default:
                return Result.Fail($"Unknown layout transition: {transition}");
        }
    }

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

        // Manually changing panel visibility means the user is customizing the layout, so leave any
        // Focus/Presentation mode and return to the Default layout.
        if (_layoutMode != LayoutMode.Default)
        {
            SetLayoutModeInternal(LayoutMode.Default);
        }
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
        // The window has exited fullscreen via drag, so sync our fullscreen state. The layout mode is
        // independent of fullscreen and is left unchanged.
        if (_isFullScreen)
        {
            _logger.LogDebug("Detected fullscreen exit via drag, clearing fullscreen state");
            SetFullScreenInternal(false);
        }
    }

    private Result TransitionToLayoutMode(LayoutMode mode)
    {
        if (_layoutMode == mode)
        {
            return Result.Ok();
        }

        // Default restores the user's preferred panels; Focus and Presentation both hide the side
        // panels (keeping a maximized console so the user can keep working in it). They differ only in
        // the toolbar and document tabs, which the views hide based on the layout mode.
        LayoutRegion targetVisibility;
        if (mode == LayoutMode.Default)
        {
            targetVisibility = PreferredRegionVisibility;
        }
        else
        {
            targetVisibility = IsConsoleMaximized
                ? LayoutRegion.Console
                : LayoutRegion.None;
        }

        // Mode-driven visibility is transient, so it is not persisted as the preferred configuration.
        UpdateRegionVisibility(targetVisibility, shouldPersist: false);
        SetLayoutModeInternal(mode);

        return Result.Ok();
    }

    private Result HandleToggleFocus()
    {
        var target = _layoutMode == LayoutMode.Default
            ? LayoutMode.Focus
            : LayoutMode.Default;

        return TransitionToLayoutMode(target);
    }

    private Result HandleToggleFullScreen()
    {
        SetFullScreenInternal(!_isFullScreen);
        return Result.Ok();
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

        // Return to the Default layout and exit fullscreen.
        if (_layoutMode != LayoutMode.Default)
        {
            SetLayoutModeInternal(LayoutMode.Default);
        }

        if (_isFullScreen)
        {
            SetFullScreenInternal(false);
        }

        // Sync the window state (e.g., restore from maximized).
        _messengerService.Send(new RestoreWindowStateMessage());

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
        // and not in Presentation mode (temporary presentation state)
        if (shouldPersist &&
            _layoutMode != LayoutMode.Presentation)
        {
            PersistPreferredRegionVisibility(newVisibility);
        }

        // Broadcast the change
        var message = new RegionVisibilityChangedMessage(newVisibility);
        _messengerService.Send(message);

        _logger.LogDebug($"Panel visibility changed: {oldVisibility} -> {newVisibility} (persist: {shouldPersist})");
    }

    private void SetLayoutModeInternal(LayoutMode newMode)
    {
        if (_layoutMode == newMode)
        {
            return;
        }

        var oldMode = _layoutMode;
        _layoutMode = newMode;

        var message = new LayoutModeChangedMessage(newMode);
        _messengerService.Send(message);

        _logger.LogDebug($"Layout mode changed: {oldMode} -> {newMode}");
    }

    private void SetFullScreenInternal(bool isFullScreen)
    {
        if (_isFullScreen == isFullScreen)
        {
            return;
        }

        _isFullScreen = isFullScreen;

        var message = new FullScreenChangedMessage(isFullScreen);
        _messengerService.Send(message);

        _logger.LogDebug($"Fullscreen changed: {isFullScreen}");
    }
}
