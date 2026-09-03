using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Settings;
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
    private readonly ViewMenuViewModel _viewMenuViewModel;

    public ApplicationMenuViewModel ViewModel { get; }

    public MainMenu(MenuFlyout menuFlyout)
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<ApplicationMenuViewModel>();
        _viewMenuViewModel = ServiceLocator.AcquireService<ViewMenuViewModel>();

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

        // The named groups mirror the macOS menu bar (see MacOSMainMenu), folded into submenus so the
        // hamburger reads the same way on Windows and Linux. Single app-level commands stay flat below them.
        _menuFlyout.Items.Add(CreateFileSubItem());

        // Edit verbs route to the focused surface through the edit-intent command; enable state reflects what
        // that surface can currently do.
        _menuFlyout.Items.Add(CreateEditSubItem());

        _menuFlyout.Items.Add(CreateViewSubItem());

        _menuFlyout.Items.Add(new MenuFlyoutSeparator());

        var settingsItem = CreateMenuItem(
            iconSymbol: IconSymbol.Settings,
            label: _stringLocalizer.GetString("MainMenu_Settings"),
            isEnabled: true,
            onClick: (sender, e) => ViewModel.ShowSettings());
        _menuFlyout.Items.Add(settingsItem);

        _menuFlyout.Items.Add(new MenuFlyoutSeparator());

        var exitItem = CreateMenuItem(
            iconSymbol: IconSymbol.Exit,
            label: _stringLocalizer.GetString("MainMenu_Exit"),
            isEnabled: true,
            onClick: (sender, e) => ViewModel.ExitApplication());
        _menuFlyout.Items.Add(exitItem);
    }

    private MenuFlyoutSubItem CreateFileSubItem()
    {
        var isWorkspaceLoaded = ViewModel.IsWorkspaceLoaded;

        var fileSubItem = new MenuFlyoutSubItem
        {
            Text = _stringLocalizer.GetString("Menu_File")
        };

        fileSubItem.Items.Add(CreateMenuItem(
            iconSymbol: IconSymbol.FolderAdd,
            label: _stringLocalizer.GetString("MainMenu_NewProject"),
            isEnabled: true,
            onClick: (sender, e) => ViewModel.NewProject()));

        // New File creates a file in the Explorer's selected folder (or the project root). Enabled only while a
        // workspace is loaded.
        fileSubItem.Items.Add(CreateMenuItem(
            iconSymbol: IconSymbol.FileAdd,
            label: _stringLocalizer.GetString("MainMenu_NewFile"),
            isEnabled: isWorkspaceLoaded,
            onClick: (sender, e) => ExecuteCreateResource(ResourceType.File)));

        fileSubItem.Items.Add(CreateMenuItem(
            iconSymbol: IconSymbol.FolderAdd,
            label: _stringLocalizer.GetString("MainMenu_NewFolder"),
            isEnabled: isWorkspaceLoaded,
            onClick: (sender, e) => ExecuteCreateResource(ResourceType.Folder)));

        fileSubItem.Items.Add(new MenuFlyoutSeparator());

        fileSubItem.Items.Add(CreateMenuItem(
            iconSymbol: IconSymbol.FolderOpen,
            label: _stringLocalizer.GetString("MainMenu_OpenProject"),
            isEnabled: true,
            onClick: (sender, e) => ViewModel.OpenProject()));

        fileSubItem.Items.Add(CreateOpenRecentSubItem());

        fileSubItem.Items.Add(new MenuFlyoutSeparator());

        fileSubItem.Items.Add(CreateMenuItem(
            iconSymbol: IconSymbol.Refresh,
            label: _stringLocalizer.GetString("MainMenu_ReloadProject"),
            isEnabled: isWorkspaceLoaded,
            onClick: (sender, e) => ViewModel.ReloadProject()));

        fileSubItem.Items.Add(CreateMenuItem(
            iconSymbol: IconSymbol.Close,
            label: _stringLocalizer.GetString("MainMenu_CloseProject"),
            isEnabled: isWorkspaceLoaded,
            onClick: (sender, e) => _ = ViewModel.CloseProjectAsync()));

        fileSubItem.Items.Add(new MenuFlyoutSeparator());

        // Scans the project for project: references that no longer resolve and opens the findings as a report.
        fileSubItem.Items.Add(CreateMenuItem(
            iconSymbol: IconSymbol.Link,
            label: _stringLocalizer.GetString("MainMenu_CheckReferences"),
            isEnabled: isWorkspaceLoaded,
            onClick: (sender, e) => ExecuteCheckReferences()));

        // Reveals the current run's log file in the file manager. Always enabled, since the log is useful
        // even when no project is loaded.
        fileSubItem.Items.Add(CreateMenuItem(
            iconSymbol: IconSymbol.Bug,
            label: _stringLocalizer.GetString("MainMenu_ShowLog"),
            isEnabled: true,
            onClick: (sender, e) => ViewModel.ShowLogs()));

        return fileSubItem;
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

        RecentProjectsMenu.Populate(
            openRecentSubItem.Items,
            recentProjects,
            OpenRecentProject,
            _stringLocalizer.GetString("MainMenu_ClearRecentProjects"),
            ViewModel.ClearRecentProjects);

        return openRecentSubItem;
    }

    private MenuFlyoutSubItem CreateEditSubItem()
    {
        var focusService = ServiceLocator.AcquireService<IFocusService>();
        var shortcutHintService = ServiceLocator.AcquireService<IShortcutHintService>();
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
                IsEnabled = isEnabled,

                // Display only. The focused surface handles the chord itself.
                KeyboardAcceleratorTextOverride = shortcutHintService.GetText(intent)
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

    private MenuFlyoutSubItem CreateViewSubItem()
    {
        // Everything here except the theme acts on the workspace areas.
        var isWorkspaceLoaded = _viewMenuViewModel.IsWorkspaceLoaded;

        // Unlike the File submenu, the items here carry no icons: a check mark and an icon share the same
        // leading column, so the submenu gives that column over to the check glyph.
        var viewSubItem = new MenuFlyoutSubItem
        {
            Text = _stringLocalizer.GetString("Menu_View")
        };

        void AddLayoutModeItem(string labelKey, LayoutMode layoutMode)
        {
            var isCurrentMode = _viewMenuViewModel.LayoutMode == layoutMode;

            var modeItem = CreateToggleMenuItem(
                label: _stringLocalizer.GetString(labelKey),
                isChecked: isCurrentMode,
                isEnabled: isWorkspaceLoaded,
                onClick: (sender, e) => _viewMenuViewModel.SetLayoutMode(layoutMode));

            viewSubItem.Items.Add(modeItem);
        }

        AddLayoutModeItem("LayoutToolbar_DefaultLabel", LayoutMode.Default);
        AddLayoutModeItem("LayoutToolbar_FocusLabel", LayoutMode.Focus);
        AddLayoutModeItem("LayoutToolbar_PresentationLabel", LayoutMode.Presentation);

        viewSubItem.Items.Add(new MenuFlyoutSeparator());

        void AddAreaItem(string labelKey, WorkspaceArea area)
        {
            var isVisible = _viewMenuViewModel.IsAreaVisible(area);

            var areaItem = CreateToggleMenuItem(
                label: _stringLocalizer.GetString(labelKey),
                isChecked: isVisible,
                isEnabled: isWorkspaceLoaded,
                onClick: (sender, e) => _viewMenuViewModel.SetAreaVisibility(area, !isVisible));

            viewSubItem.Items.Add(areaItem);
        }

        AddAreaItem("Menu_UtilityArea", WorkspaceArea.Utility);
        AddAreaItem("Menu_BottomArea", WorkspaceArea.Bottom);
        AddAreaItem("Menu_SideArea", WorkspaceArea.Side);

        viewSubItem.Items.Add(new MenuFlyoutSeparator());

        // Full Screen and Reset Layout act on the window as a whole rather than on one area, so they
        // group together as they do in the layout flyout. Where the platform supplies fullscreen through
        // the window chrome the app offers no toggle of its own (see LayoutToolbar, which hides the same
        // control).
        var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        if (!platformInfo.HasNativeFullScreenAffordance)
        {
            var fullScreenItem = CreateToggleMenuItem(
                label: _stringLocalizer.GetString("LayoutToolbar_FullScreen"),
                isChecked: _viewMenuViewModel.IsFullScreen,
                isEnabled: true,
                onClick: (sender, e) => _viewMenuViewModel.ToggleFullScreen());

            viewSubItem.Items.Add(fullScreenItem);
        }

        var resetLayoutItem = new MenuFlyoutItem
        {
            Text = _stringLocalizer.GetString("LayoutToolbar_ResetLayoutButton"),
            IsEnabled = isWorkspaceLoaded
        };
        resetLayoutItem.Click += (sender, e) => _viewMenuViewModel.ResetLayout();
        viewSubItem.Items.Add(resetLayoutItem);

        viewSubItem.Items.Add(new MenuFlyoutSeparator());

        viewSubItem.Items.Add(CreateThemeSubItem());

        return viewSubItem;
    }

    private MenuFlyoutSubItem CreateThemeSubItem()
    {
        var themeSubItem = new MenuFlyoutSubItem
        {
            Text = _stringLocalizer.GetString("Menu_Theme")
        };

        // The stored theme can be System as well as a fixed Light or Dark, so all three are offered rather
        // than a single toggle. Mirrors the Settings page theme options.
        var currentTheme = _viewMenuViewModel.Theme;

        foreach (var theme in Enum.GetValues<ApplicationColorTheme>())
        {
            var themeItem = CreateToggleMenuItem(
                label: _stringLocalizer.GetString("Theme_" + theme),
                isChecked: theme == currentTheme,
                isEnabled: true,
                onClick: (sender, e) => _viewMenuViewModel.SetTheme(theme));

            themeSubItem.Items.Add(themeItem);
        }

        return themeSubItem;
    }

    private ToggleMenuFlyoutItem CreateToggleMenuItem(
        string label,
        bool isChecked,
        bool isEnabled,
        RoutedEventHandler onClick)
    {
        var menuItem = new ToggleMenuFlyoutItem
        {
            Text = label,
            IsChecked = isChecked,
            IsEnabled = isEnabled
        };
        menuItem.Click += onClick;

        return menuItem;
    }

    private void ExecuteCreateResource(ResourceType resourceType)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();
        commandService.Execute<ICreateResourceDialogCommand>(command =>
        {
            command.ResourceType = resourceType;
        });
    }

    private void ExecuteCheckReferences()
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();
        commandService.Execute<ICheckReferencesCommand>(command => command.OpenReport = true);
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
