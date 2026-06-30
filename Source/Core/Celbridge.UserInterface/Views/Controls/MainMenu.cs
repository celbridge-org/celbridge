using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Navigation;
using Celbridge.UserInterface.Views.Controls;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// Helper class that creates and manages the MainMenu NavigationViewItem.
/// </summary>
public class MainMenu
{
    private const string MenuTag = "Menu";
    private const string NewProjectTag = "NewProject";
    private const string NewFileTag = "NewFile";
    private const string NewFolderTag = "NewFolder";
    private const string OpenProjectTag = "OpenProject";
    private const string OpenRecentTag = "OpenRecent";
    private const string RecentProjectTagPrefix = "RecentProject_";
    private const string ClearRecentProjectsTag = "ClearRecentProjects";
    private const string ReloadProjectTag = "ReloadProject";
    private const string CloseProjectTag = "CloseProject";
    private const string ExitTag = "Exit";

    private const string EditMenuTag = "EditMenu";
    private const string EditUndoTag = "EditUndo";
    private const string EditRedoTag = "EditRedo";
    private const string EditCutTag = "EditCut";
    private const string EditCopyTag = "EditCopy";
    private const string EditPasteTag = "EditPaste";
    private const string EditSelectAllTag = "EditSelectAll";

    private readonly IStringLocalizer _stringLocalizer;
    private readonly NavigationViewItem _menuNavItem;

    public MainMenuViewModel ViewModel { get; }

    /// <summary>
    /// Event raised when a menu item is invoked. The parent control should handle closing flyouts.
    /// </summary>
    public event EventHandler? MenuItemInvoked;

    public MainMenu()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<MainMenuViewModel>();

        _menuNavItem = new NavigationViewItem
        {
            Tag = MenuTag,
            Icon = new Icon { Symbol = IconSymbol.Menu }
        };
        _menuNavItem.SetValue(NavigationViewItem.SelectsOnInvokedProperty, false);

        var menuTooltip = _stringLocalizer.GetString("TitleBar_MainMenuTooltip");
        ToolTipService.SetToolTip(_menuNavItem, menuTooltip);
        ToolTipService.SetPlacement(_menuNavItem, PlacementMode.Bottom);

        // Build initial menu items
        RebuildMenuItems();

        // Rebuild menu items when the flyout is about to open to ensure correct state
        _menuNavItem.RegisterPropertyChangedCallback(NavigationViewItem.IsExpandedProperty, OnMenuExpandedChanged);
    }

    private void OnMenuExpandedChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (_menuNavItem.IsExpanded)
        {
            // Rebuild menu items when the menu opens to ensure correct visual state
            RebuildMenuItems();
        }
    }

    private void RebuildMenuItems()
    {
        _menuNavItem.MenuItems.Clear();

        var isWorkspaceLoaded = ViewModel.IsWorkspaceLoaded;

        // New Project
        var newProjectNavItem = CreateMenuItem(
            tag: NewProjectTag,
            icon: new Icon { Symbol = IconSymbol.FolderAdd },
            label: _stringLocalizer.GetString("MainMenu_NewProject"),
            tooltip: _stringLocalizer.GetString("MainMenu_NewProjectTooltip"),
            isEnabled: true);
        _menuNavItem.MenuItems.Add(newProjectNavItem);

        // New File, surfaced for parity with the macOS File menu. Creates a file in the Explorer's selected
        // folder (or the project root). Enabled only while a workspace is loaded.
        var newFileNavItem = CreateMenuItem(
            tag: NewFileTag,
            icon: new Icon { Symbol = IconSymbol.FileAdd },
            label: _stringLocalizer.GetString("MainMenu_NewFile"),
            tooltip: _stringLocalizer.GetString("MainMenu_NewFileTooltip"),
            isEnabled: isWorkspaceLoaded);
        _menuNavItem.MenuItems.Add(newFileNavItem);

        // New Folder
        var newFolderNavItem = CreateMenuItem(
            tag: NewFolderTag,
            icon: new Icon { Symbol = IconSymbol.FolderAdd },
            label: _stringLocalizer.GetString("MainMenu_NewFolder"),
            tooltip: _stringLocalizer.GetString("MainMenu_NewFolderTooltip"),
            isEnabled: isWorkspaceLoaded);
        _menuNavItem.MenuItems.Add(newFolderNavItem);

        // Open Project
        var openProjectNavItem = CreateMenuItem(
            tag: OpenProjectTag,
            icon: new Icon { Symbol = IconSymbol.FolderOpen },
            label: _stringLocalizer.GetString("MainMenu_OpenProject"),
            tooltip: _stringLocalizer.GetString("MainMenu_OpenProjectTooltip"),
            isEnabled: true);
        _menuNavItem.MenuItems.Add(openProjectNavItem);

        // Open Recent submenu
        var openRecentNavItem = CreateOpenRecentMenuItem();
        _menuNavItem.MenuItems.Add(openRecentNavItem);

        // Reload Project
        var reloadProjectNavItem = CreateMenuItem(
            tag: ReloadProjectTag,
            icon: new Icon { Symbol = IconSymbol.Refresh },
            label: _stringLocalizer.GetString("MainMenu_ReloadProject"),
            tooltip: _stringLocalizer.GetString("MainMenu_ReloadProjectTooltip"),
            isEnabled: isWorkspaceLoaded);
        _menuNavItem.MenuItems.Add(reloadProjectNavItem);

        // Close Project
        var closeProjectNavItem = CreateMenuItem(
            tag: CloseProjectTag,
            icon: new Icon { Symbol = IconSymbol.Close },
            label: _stringLocalizer.GetString("MainMenu_CloseProject"),
            tooltip: _stringLocalizer.GetString("MainMenu_CloseProjectTooltip"),
            isEnabled: isWorkspaceLoaded);
        _menuNavItem.MenuItems.Add(closeProjectNavItem);

        _menuNavItem.MenuItems.Add(new NavigationViewItemSeparator());

        // Edit verbs, surfaced for parity with the macOS Edit menu. Each routes to the focused surface
        // through the edit-intent command. Enable state reflects what that surface can currently do.
        var editNavItem = CreateEditMenuItem();
        _menuNavItem.MenuItems.Add(editNavItem);

        _menuNavItem.MenuItems.Add(new NavigationViewItemSeparator());

        // Settings
        var settingsNavItem = CreateMenuItem(
            tag: NavigationConstants.SettingsTag,
            icon: new Icon { Symbol = IconSymbol.Settings },
            label: _stringLocalizer.GetString("MainMenu_Settings"),
            tooltip: _stringLocalizer.GetString("MainMenu_SettingsTooltip"),
            isEnabled: true);
        _menuNavItem.MenuItems.Add(settingsNavItem);

        _menuNavItem.MenuItems.Add(new NavigationViewItemSeparator());



        // Exit
        var exitNavItem = CreateMenuItem(
            tag: ExitTag,
            icon: new Icon { Symbol = IconSymbol.Exit },
            label: _stringLocalizer.GetString("MainMenu_Exit"),
            tooltip: _stringLocalizer.GetString("MainMenu_ExitTooltip"),
            isEnabled: true);
        _menuNavItem.MenuItems.Add(exitNavItem);
    }

    private NavigationViewItem CreateOpenRecentMenuItem()
    {
        var recentProjects = ViewModel.GetRecentProjects();
        var hasRecentProjects = recentProjects.Count > 0;

        var openRecentNavItem = new NavigationViewItem
        {
            Tag = OpenRecentTag,
            Icon = new Icon { Symbol = IconSymbol.Recent },
            Content = _stringLocalizer.GetString("MainMenu_OpenRecent"),
            IsEnabled = hasRecentProjects
        };
        openRecentNavItem.SetValue(NavigationViewItem.SelectsOnInvokedProperty, false);

        ToolTipService.SetToolTip(openRecentNavItem, _stringLocalizer.GetString("MainMenu_OpenRecentTooltip"));
        ToolTipService.SetPlacement(openRecentNavItem, PlacementMode.Right);

        if (hasRecentProjects)
        {
            // Add recent project items showing project name with full path in tooltip
            foreach (var recentProject in recentProjects)
            {
                var projectNavItem = new NavigationViewItem
                {
                    Tag = RecentProjectTagPrefix + recentProject.ProjectFilePath,
                    Content = recentProject.ProjectName
                };
                projectNavItem.SetValue(NavigationViewItem.SelectsOnInvokedProperty, false);

                // Show full path in tooltip
                ToolTipService.SetToolTip(projectNavItem, recentProject.ProjectFilePath);
                ToolTipService.SetPlacement(projectNavItem, PlacementMode.Right);

                openRecentNavItem.MenuItems.Add(projectNavItem);
            }

            // Add separator before "Clear recently opened"
            openRecentNavItem.MenuItems.Add(new NavigationViewItemSeparator());

            // Add "Clear recently opened" option
            var clearRecentNavItem = new NavigationViewItem
            {
                Tag = ClearRecentProjectsTag,
                Icon = new Icon { Symbol = IconSymbol.Delete },
                Content = _stringLocalizer.GetString("MainMenu_ClearRecentProjects")
            };
            clearRecentNavItem.SetValue(NavigationViewItem.SelectsOnInvokedProperty, false);

            ToolTipService.SetToolTip(clearRecentNavItem, _stringLocalizer.GetString("MainMenu_ClearRecentProjectsTooltip"));
            ToolTipService.SetPlacement(clearRecentNavItem, PlacementMode.Right);

            openRecentNavItem.MenuItems.Add(clearRecentNavItem);
        }

        return openRecentNavItem;
    }

    private NavigationViewItem CreateEditMenuItem()
    {
        var focusService = ServiceLocator.AcquireService<IFocusService>();
        var activeTarget = focusService.EditTarget;

        var editNavItem = new NavigationViewItem
        {
            Tag = EditMenuTag,
            Content = _stringLocalizer.GetString("Menu_Edit")
        };
        editNavItem.SetValue(NavigationViewItem.SelectsOnInvokedProperty, false);
        ToolTipService.SetPlacement(editNavItem, PlacementMode.Right);

        void AddEditItem(string tag, string labelKey, EditIntent intent)
        {
            var item = new NavigationViewItem
            {
                Tag = tag,
                Content = _stringLocalizer.GetString(labelKey),
                IsEnabled = activeTarget is not null
                    && activeTarget.CanPerformEdit(intent)
            };
            item.SetValue(NavigationViewItem.SelectsOnInvokedProperty, false);
            ToolTipService.SetPlacement(item, PlacementMode.Right);
            editNavItem.MenuItems.Add(item);
        }

        AddEditItem(EditUndoTag, "Menu_Undo", EditIntent.Undo);
        AddEditItem(EditRedoTag, "Menu_Redo", EditIntent.Redo);
        editNavItem.MenuItems.Add(new NavigationViewItemSeparator());
        AddEditItem(EditCutTag, "Menu_Cut", EditIntent.Cut);
        AddEditItem(EditCopyTag, "Menu_Copy", EditIntent.Copy);
        AddEditItem(EditPasteTag, "Menu_Paste", EditIntent.Paste);
        AddEditItem(EditSelectAllTag, "Menu_SelectAll", EditIntent.SelectAll);

        return editNavItem;
    }

    private static EditIntent? EditIntentForTag(string tag)
    {
        return tag switch
        {
            EditUndoTag => EditIntent.Undo,
            EditRedoTag => EditIntent.Redo,
            EditCutTag => EditIntent.Cut,
            EditCopyTag => EditIntent.Copy,
            EditPasteTag => EditIntent.Paste,
            EditSelectAllTag => EditIntent.SelectAll,
            _ => null
        };
    }

    private NavigationViewItem CreateMenuItem(string tag, IconElement icon, string label, string tooltip, bool isEnabled)
    {
        var navItem = new NavigationViewItem
        {
            Tag = tag,
            Icon = icon,
            Content = label,
            IsEnabled = isEnabled
        };
        navItem.SetValue(NavigationViewItem.SelectsOnInvokedProperty, false);

        ToolTipService.SetToolTip(navItem, tooltip);
        ToolTipService.SetPlacement(navItem, PlacementMode.Right);

        return navItem;
    }

    public void OnLoaded()
    {
        ViewModel.OnLoaded();
    }

    public void OnUnloaded()
    {
        ViewModel.OnUnloaded();
    }

    /// <summary>
    /// Handles item invoked events from the parent NavigationView.
    /// Call this from the parent's ItemInvoked handler.
    /// </summary>
    public async void HandleItemInvoked(NavigationViewItem invokedItem)
    {
        var tag = invokedItem.Tag?.ToString();
        if (string.IsNullOrEmpty(tag))
        {
            return;
        }

        // Handle recent project items
        if (tag.StartsWith(RecentProjectTagPrefix))
        {
            var projectFilePath = tag.Substring(RecentProjectTagPrefix.Length);
            await ViewModel.OpenRecentProjectAsync(projectFilePath);
            MenuItemInvoked?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Edit verbs route to the focused surface through the edit-intent command, the same path the
        // keyboard and the macOS Edit menu use.
        var editIntent = EditIntentForTag(tag);
        if (editIntent is not null)
        {
            var commandService = ServiceLocator.AcquireService<ICommandService>();
            commandService.Execute<IPerformEditCommand>(command => command.Intent = editIntent.Value);
            MenuItemInvoked?.Invoke(this, EventArgs.Empty);
            return;
        }

        switch (tag)
        {
            case MenuTag:
                // Menu item just opens its flyout, no action needed
                break;

            case NewProjectTag:
                ViewModel.NewProject();
                MenuItemInvoked?.Invoke(this, EventArgs.Empty);
                break;

            case OpenProjectTag:
                ViewModel.OpenProject();
                MenuItemInvoked?.Invoke(this, EventArgs.Empty);
                break;

            case NewFileTag:
                ServiceLocator.AcquireService<ICommandService>().Execute<ICreateResourceDialogCommand>(command =>
                {
                    command.ResourceType = ResourceType.File;
                });
                MenuItemInvoked?.Invoke(this, EventArgs.Empty);
                break;

            case NewFolderTag:
                ServiceLocator.AcquireService<ICommandService>().Execute<ICreateResourceDialogCommand>(command =>
                {
                    command.ResourceType = ResourceType.Folder;
                });
                MenuItemInvoked?.Invoke(this, EventArgs.Empty);
                break;

            case OpenRecentTag:
                // Open Recent submenu just opens its flyout, no action needed
                break;

            case ClearRecentProjectsTag:
                ViewModel.ClearRecentProjects();
                MenuItemInvoked?.Invoke(this, EventArgs.Empty);
                break;

            case ReloadProjectTag:
                _ = ViewModel.ReloadProjectAsync();
                MenuItemInvoked?.Invoke(this, EventArgs.Empty);
                break;

            case CloseProjectTag:
                _ = ViewModel.CloseProjectAsync();
                MenuItemInvoked?.Invoke(this, EventArgs.Empty);
                break;

            case NavigationConstants.SettingsTag:
                ViewModel.NavigateToSettings();
                MenuItemInvoked?.Invoke(this, EventArgs.Empty);
                break;

            case ExitTag:
                ViewModel.ExitApplication();
                MenuItemInvoked?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    /// <summary>
    /// Returns the root NavigationViewItem for embedding in a NavigationView.
    /// </summary>
    public NavigationViewItem GetMenuNavItem() => _menuNavItem;
}
