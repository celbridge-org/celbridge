using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Navigation;
using Celbridge.UserInterface.Views.Controls;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// Builds and manages the main menu flyout hosted by the title-bar menu button. Used on platforms without a
/// native menu bar (Windows, Linux); macOS surfaces the same commands through MacOSMainMenu.
/// </summary>
public class MainMenu
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly MenuFlyout _menuFlyout;

    public MainMenuViewModel ViewModel { get; }

    public MainMenu(MenuFlyout menuFlyout)
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<MainMenuViewModel>();

        _menuFlyout = menuFlyout;

        // Rebuild the items each time the flyout opens so enable states and the recent-projects list are current.
        _menuFlyout.Opening += OnMenuFlyoutOpening;

        RebuildMenuItems();
    }

    private void OnMenuFlyoutOpening(object? sender, object args)
    {
        RebuildMenuItems();
    }

    private void RebuildMenuItems()
    {
        _menuFlyout.Items.Clear();

        var isWorkspaceLoaded = ViewModel.IsWorkspaceLoaded;

        var newProjectItem = CreateMenuItem(
            iconSymbol: IconSymbol.FolderAdd,
            label: _stringLocalizer.GetString("MainMenu_NewProject"),
            isEnabled: true,
            onClick: (sender, e) => ViewModel.NewProject());
        _menuFlyout.Items.Add(newProjectItem);

        // New File, surfaced for parity with the macOS File menu. Creates a file in the Explorer's selected
        // folder (or the project root). Enabled only while a workspace is loaded.
        var newFileItem = CreateMenuItem(
            iconSymbol: IconSymbol.FileAdd,
            label: _stringLocalizer.GetString("MainMenu_NewFile"),
            isEnabled: isWorkspaceLoaded,
            onClick: (sender, e) => ExecuteCreateResource(ResourceType.File));
        _menuFlyout.Items.Add(newFileItem);

        var newFolderItem = CreateMenuItem(
            iconSymbol: IconSymbol.FolderAdd,
            label: _stringLocalizer.GetString("MainMenu_NewFolder"),
            isEnabled: isWorkspaceLoaded,
            onClick: (sender, e) => ExecuteCreateResource(ResourceType.Folder));
        _menuFlyout.Items.Add(newFolderItem);

        var openProjectItem = CreateMenuItem(
            iconSymbol: IconSymbol.FolderOpen,
            label: _stringLocalizer.GetString("MainMenu_OpenProject"),
            isEnabled: true,
            onClick: (sender, e) => ViewModel.OpenProject());
        _menuFlyout.Items.Add(openProjectItem);

        var openRecentSubItem = CreateOpenRecentSubItem();
        _menuFlyout.Items.Add(openRecentSubItem);

        var reloadProjectItem = CreateMenuItem(
            iconSymbol: IconSymbol.Refresh,
            label: _stringLocalizer.GetString("MainMenu_ReloadProject"),
            isEnabled: isWorkspaceLoaded,
            onClick: (sender, e) => ViewModel.ReloadProject());
        _menuFlyout.Items.Add(reloadProjectItem);

        var closeProjectItem = CreateMenuItem(
            iconSymbol: IconSymbol.Close,
            label: _stringLocalizer.GetString("MainMenu_CloseProject"),
            isEnabled: isWorkspaceLoaded,
            onClick: (sender, e) => _ = ViewModel.CloseProjectAsync());
        _menuFlyout.Items.Add(closeProjectItem);

        _menuFlyout.Items.Add(new MenuFlyoutSeparator());

        // Edit verbs, surfaced for parity with the macOS Edit menu. Each routes to the focused surface through
        // the edit-intent command. Enable state reflects what that surface can currently do.
        var editSubItem = CreateEditSubItem();
        _menuFlyout.Items.Add(editSubItem);

        _menuFlyout.Items.Add(new MenuFlyoutSeparator());

        var settingsItem = CreateMenuItem(
            iconSymbol: IconSymbol.Settings,
            label: _stringLocalizer.GetString("MainMenu_Settings"),
            isEnabled: true,
            onClick: (sender, e) => ViewModel.NavigateToSettings());
        _menuFlyout.Items.Add(settingsItem);

        // Show Application Logs, an app-level diagnostic that reveals the current log file in the file manager.
        // Always enabled, since logs are useful even when no project is loaded.
        var showLogsItem = CreateMenuItem(
            iconSymbol: IconSymbol.Bug,
            label: _stringLocalizer.GetString("MainMenu_ShowLogs"),
            isEnabled: true,
            onClick: (sender, e) => ViewModel.ShowLogs());
        _menuFlyout.Items.Add(showLogsItem);

        _menuFlyout.Items.Add(new MenuFlyoutSeparator());

        var exitItem = CreateMenuItem(
            iconSymbol: IconSymbol.Exit,
            label: _stringLocalizer.GetString("MainMenu_Exit"),
            isEnabled: true,
            onClick: (sender, e) => ViewModel.ExitApplication());
        _menuFlyout.Items.Add(exitItem);
    }

    private MenuFlyoutItem CreateMenuItem(
        IconSymbol iconSymbol,
        string label,
        bool isEnabled,
        RoutedEventHandler onClick)
    {
        var menuItem = new MenuFlyoutItem
        {
            Text = label,
            Icon = new Icon { Symbol = iconSymbol },
            IsEnabled = isEnabled
        };
        menuItem.Click += onClick;

        return menuItem;
    }

    private MenuFlyoutSubItem CreateOpenRecentSubItem()
    {
        var openRecentSubItem = new MenuFlyoutSubItem
        {
            Text = _stringLocalizer.GetString("MainMenu_OpenRecent"),
            Icon = new Icon { Symbol = IconSymbol.Recent }
        };

        var recentProjects = ViewModel.GetRecentProjects();
        if (recentProjects.Count == 0)
        {
            openRecentSubItem.IsEnabled = false;
            return openRecentSubItem;
        }

        foreach (var recentProject in recentProjects)
        {
            var projectFilePath = recentProject.ProjectFilePath;

            var projectItem = new MenuFlyoutItem
            {
                Text = recentProject.ProjectName
            };
            ToolTipService.SetToolTip(projectItem, projectFilePath);
            projectItem.Click += (sender, e) => OpenRecentProject(projectFilePath);

            openRecentSubItem.Items.Add(projectItem);
        }

        openRecentSubItem.Items.Add(new MenuFlyoutSeparator());

        var clearRecentItem = new MenuFlyoutItem
        {
            Text = _stringLocalizer.GetString("MainMenu_ClearRecentProjects"),
            Icon = new Icon { Symbol = IconSymbol.Delete }
        };
        clearRecentItem.Click += (sender, e) => ViewModel.ClearRecentProjects();

        openRecentSubItem.Items.Add(clearRecentItem);

        return openRecentSubItem;
    }

    private MenuFlyoutSubItem CreateEditSubItem()
    {
        var focusService = ServiceLocator.AcquireService<IFocusService>();
        var activeTarget = focusService.EditTarget;

        var editSubItem = new MenuFlyoutSubItem
        {
            Text = _stringLocalizer.GetString("Menu_Edit")
        };

        void AddEditItem(string labelKey, EditIntent intent)
        {
            var isEnabled = activeTarget is not null
                && activeTarget.CanPerformEdit(intent);

            var editItem = new MenuFlyoutItem
            {
                Text = _stringLocalizer.GetString(labelKey),
                IsEnabled = isEnabled
            };
            editItem.Click += (sender, e) => PerformEdit(intent);

            editSubItem.Items.Add(editItem);
        }

        AddEditItem("Menu_Undo", EditIntent.Undo);
        AddEditItem("Menu_Redo", EditIntent.Redo);
        editSubItem.Items.Add(new MenuFlyoutSeparator());
        AddEditItem("Menu_Cut", EditIntent.Cut);
        AddEditItem("Menu_Copy", EditIntent.Copy);
        AddEditItem("Menu_Paste", EditIntent.Paste);
        AddEditItem("Menu_SelectAll", EditIntent.SelectAll);

        return editSubItem;
    }

    private void ExecuteCreateResource(ResourceType resourceType)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();
        commandService.Execute<ICreateResourceDialogCommand>(command =>
        {
            command.ResourceType = resourceType;
        });
    }

    private void PerformEdit(EditIntent intent)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();
        commandService.Execute<IPerformEditCommand>(command => command.Intent = intent);
    }

    private async void OpenRecentProject(string projectFilePath)
    {
        // async void: observe exceptions here so a failed open (e.g. the recent project moved or was deleted)
        // cannot crash on the UI thread.
        try
        {
            await ViewModel.OpenRecentProjectAsync(projectFilePath);
        }
        catch (Exception ex)
        {
            var logger = ServiceLocator.AcquireService<ILogger<MainMenu>>();
            logger.LogError(ex, "Failed to open recent project");
        }
    }

    public void OnLoaded()
    {
        ViewModel.OnLoaded();
    }

    public void OnUnloaded()
    {
        _menuFlyout.Opening -= OnMenuFlyoutOpening;
        ViewModel.OnUnloaded();
    }
}
