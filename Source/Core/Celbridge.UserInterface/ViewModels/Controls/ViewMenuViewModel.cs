using Celbridge.Commands;
using Celbridge.Settings;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// Shared view model for the View menu. Holds no state of its own, so both the in-window menu and the
/// native macOS menubar read the current state each time their menu opens.
/// </summary>
public class ViewMenuViewModel
{
    private readonly ICommandService _commandService;
    private readonly ISettingsService _settingsService;
    private readonly IWindowModeService _windowModeService;
    private readonly ILayoutService _layoutService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ViewMenuViewModel(
        ICommandService commandService,
        ISettingsService settingsService,
        IWindowModeService windowModeService,
        ILayoutService layoutService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _settingsService = settingsService;
        _windowModeService = windowModeService;
        _layoutService = layoutService;
        _workspaceWrapper = workspaceWrapper;
    }

    /// <summary>
    /// Whether a workspace is loaded. The layout commands all act on the workspace surfaces, so they are
    /// unavailable on the other pages.
    /// </summary>
    public bool IsWorkspaceLoaded => _workspaceWrapper.IsWorkspaceLoaded;

    public LayoutMode LayoutMode => _windowModeService.LayoutMode;

    public bool IsFullScreen => _windowModeService.IsFullScreen;

    /// <summary>
    /// The selected application colour theme, which may be System rather than a fixed light or dark.
    /// </summary>
    public ApplicationColorTheme Theme => _settingsService.Get(SettingCatalog.Application.Theme);

    public bool IsAreaVisible(WorkspaceArea area)
    {
        return _layoutService.IsAreaVisible(area);
    }

    public void SetLayoutMode(LayoutMode layoutMode)
    {
        var transition = LayoutTransition.Default;
        switch (layoutMode)
        {
            case LayoutMode.Focus:
                transition = LayoutTransition.Focus;
                break;

            case LayoutMode.Presentation:
                transition = LayoutTransition.Presentation;
                break;
        }

        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = transition;
        });
    }

    public void SetAreaVisibility(WorkspaceArea area, bool isVisible)
    {
        _commandService.Execute<ISetAreaVisibilityCommand>(command =>
        {
            command.Area = area;
            command.IsVisible = isVisible;
        });
    }

    public void ToggleFullScreen()
    {
        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = LayoutTransition.ToggleFullScreen;
        });
    }

    /// <summary>
    /// Restores all panels to visible at their default sizes and returns to the Default layout.
    /// </summary>
    public void ResetLayout()
    {
        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = LayoutTransition.ResetLayout;
        });
    }

    public void SetTheme(ApplicationColorTheme theme)
    {
        _commandService.Execute<ISetThemeCommand>(command =>
        {
            command.Theme = theme;
        });
    }
}
