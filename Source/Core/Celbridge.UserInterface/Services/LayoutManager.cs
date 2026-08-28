using Celbridge.Logging;
using Celbridge.Settings;
using Celbridge.Utilities;
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

    private LayoutMode _layoutMode = LayoutMode.Default;
    private bool _isFullScreen;
    private BottomAreaAlignment _bottomAreaAlignment = WorkspaceConstants.BottomAreaAlignment;

    public LayoutManager(
        ILogger<LayoutManager> logger,
        IMessengerService messengerService,
        ISettingsService settingsService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _settingsService = settingsService;
        _workspaceWrapper = workspaceWrapper;

        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);

        // Listen for when the user exits fullscreen by dragging the window (Windows built-in behavior)
        _messengerService.Register<ExitedFullscreenViaDragMessage>(this, OnExitedFullscreenViaDrag);
    }

    // The typed workspace settings facade, or null when no workspace is loaded.
    // Panel layout is Workspace-scoped, so it has no meaning outside a project.
    private IBindableWorkspaceSettings? WorkspaceSettings =>
        _workspaceWrapper.IsWorkspaceLoaded
            ? _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings
            : null;

    // The areas the project prefers to show, falling back to every area when no workspace is loaded.
    private IReadOnlySet<WorkspaceArea> PreferredVisibleAreas =>
        WorkspaceSettings?.PreferredVisibleAreas ?? WorkspaceAreaHelper.AllAreasVisible;

    // Persists the preferred visible areas for the current project. A no-op when no workspace is loaded.
    private void PersistPreferredVisibleAreas(IReadOnlySet<WorkspaceArea> visibleAreas)
    {
        var workspaceSettings = WorkspaceSettings;
        if (workspaceSettings is not null)
        {
            workspaceSettings.PreferredVisibleAreas = visibleAreas;
        }
    }

    private static readonly IReadOnlySet<WorkspaceArea> OnlyMainVisible = new HashSet<WorkspaceArea>
    {
        WorkspaceArea.Main
    };

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        // The workspace settings are now loaded, so apply this project's preferred
        // visible areas. No need to persist, we are restoring the saved state.
        UpdateVisibleAreas(PreferredVisibleAreas, shouldPersist: false);

        var storedAlignment = WorkspaceSettings?.BottomAreaAlignment ?? WorkspaceConstants.BottomAreaAlignment;
        UpdateBottomAreaAlignment(storedAlignment, shouldPersist: false);
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

    public IReadOnlySet<WorkspaceArea> VisibleAreas { get; private set; } = WorkspaceAreaHelper.AllAreasVisible;

    public bool IsAreaVisible(WorkspaceArea area)
    {
        return VisibleAreas.Contains(area);
    }

    public Result SetAreaVisibility(WorkspaceArea area, bool isVisible)
    {
        if (!area.IsCollapsible())
        {
            return Result.Fail($"The {area} area is always visible.");
        }

        // Manually changing area visibility means the user is customizing the layout, so leave any
        // Focus/Presentation mode and return to the Default layout.
        bool isLeavingLayoutMode = _layoutMode != LayoutMode.Default;

        // Read against what is on screen, so an area hidden by a layout mode counts as revealed when the
        // user asks for it back.
        bool wasVisible = IsAreaVisible(area);

        // Those modes hide every collapsible area transiently, so the change is composed against the areas
        // the user prefers.
        var currentAreas = VisibleAreas;
        if (isLeavingLayoutMode)
        {
            currentAreas = PreferredVisibleAreas;
        }

        var newAreas = new HashSet<WorkspaceArea>(currentAreas);
        if (isVisible)
        {
            newAreas.Add(area);
        }
        else
        {
            newAreas.Remove(area);
        }

        if (newAreas.SetEquals(VisibleAreas)
            && !isLeavingLayoutMode)
        {
            return Result.Ok();
        }

        // This is a user-initiated change, so it should persist
        UpdateVisibleAreas(newAreas, shouldPersist: true);

        if (isLeavingLayoutMode)
        {
            SetLayoutModeInternal(LayoutMode.Default);
        }

        // Sent last, once the whole layout has settled, and only for the area the user asked for.
        if (isVisible
            && !wasVisible)
        {
            _messengerService.Send(new FlashAreaMessage(area));
        }

        return Result.Ok();
    }

    public Result ToggleAreaVisibility(WorkspaceArea area)
    {
        var isCurrentlyVisible = IsAreaVisible(area);

        return SetAreaVisibility(area, !isCurrentlyVisible);
    }

    public BottomAreaAlignment BottomAreaAlignment => _bottomAreaAlignment;

    public void SetBottomAreaAlignment(BottomAreaAlignment alignment)
    {
        // Alignment is a layout preference rather than a mode, so it survives Focus and Presentation
        // unchanged and is always persisted.
        UpdateBottomAreaAlignment(alignment, shouldPersist: true);
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

        // Default restores the user's preferred areas. Focus and Presentation both hide every area that
        // can be hidden. They differ only in the toolbar and document tabs, which the views hide based on
        // the layout mode.
        IReadOnlySet<WorkspaceArea> targetAreas;
        if (mode == LayoutMode.Default)
        {
            targetAreas = PreferredVisibleAreas;
        }
        else
        {
            targetAreas = OnlyMainVisible;
        }

        // Mode-driven visibility is transient, so it is not persisted as the preferred configuration.
        UpdateVisibleAreas(targetAreas, shouldPersist: false);
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
        // Reset panel and area sizes
        var workspaceSettings = WorkspaceSettings;
        if (workspaceSettings is not null)
        {
            workspaceSettings.UtilityPanelWidth = WorkspaceConstants.UtilityPanelWidth;
            workspaceSettings.SideAreaWidth = WorkspaceConstants.SideAreaWidth;
            workspaceSettings.BottomAreaHeight = WorkspaceConstants.BottomAreaHeight;
        }

        UpdateBottomAreaAlignment(WorkspaceConstants.BottomAreaAlignment, shouldPersist: true);

        // Reset preferred window geometry
        _settingsService.Set(SettingCatalog.Window.UsePreferredGeometry, false);
        _settingsService.Set(SettingCatalog.Window.PreferredX, 0);
        _settingsService.Set(SettingCatalog.Window.PreferredY, 0);
        _settingsService.Set(SettingCatalog.Window.PreferredWidth, 0);
        _settingsService.Set(SettingCatalog.Window.PreferredHeight, 0);
        _settingsService.Set(SettingCatalog.Window.IsMaximized, false);

        UpdateVisibleAreas(WorkspaceAreaHelper.AllAreasVisible, shouldPersist: true);
        PersistPreferredVisibleAreas(WorkspaceAreaHelper.AllAreasVisible);

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

    private void UpdateVisibleAreas(IReadOnlySet<WorkspaceArea> newAreas, bool shouldPersist)
    {
        if (VisibleAreas.SetEquals(newAreas))
        {
            return;
        }

        var oldAreas = VisibleAreas;
        VisibleAreas = newAreas;

        // Mode-driven visibility is transient, and its callers say so by not asking for a persist.
        if (shouldPersist)
        {
            PersistPreferredVisibleAreas(newAreas);
        }

        // Broadcast the change
        var message = new AreaVisibilityChangedMessage(newAreas);
        _messengerService.Send(message);

        _logger.LogDebug($"Visible areas changed: {DescribeAreas(oldAreas)} -> {DescribeAreas(newAreas)} (persist: {shouldPersist})");
    }

    // The areas in a stable order, so a log line reads the same for the same set.
    private static string DescribeAreas(IReadOnlySet<WorkspaceArea> areas)
    {
        var tokens = areas.Select(area => area.ToToken()).Order(StringComparer.Ordinal);

        return string.Join(',', tokens);
    }

    private void UpdateBottomAreaAlignment(BottomAreaAlignment newAlignment, bool shouldPersist)
    {
        if (_bottomAreaAlignment == newAlignment)
        {
            return;
        }

        var oldAlignment = _bottomAreaAlignment;
        _bottomAreaAlignment = newAlignment;

        if (shouldPersist)
        {
            var workspaceSettings = WorkspaceSettings;
            if (workspaceSettings is not null)
            {
                workspaceSettings.BottomAreaAlignment = newAlignment;
            }
        }

        var message = new BottomAreaAlignmentChangedMessage(newAlignment);
        _messengerService.Send(message);

        _logger.LogDebug($"Bottom area alignment changed: {oldAlignment} -> {newAlignment} (persist: {shouldPersist})");
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
