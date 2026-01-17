using Celbridge.Navigation;
using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// Helper class that creates and manages the MainMenu NavigationViewItem.
/// </summary>
public class MainMenu
{
    private const string MenuTag = "Menu";
    private const string NewProjectTag = "NewProject";
    private const string OpenProjectTag = "OpenProject";
    private const string OpenRecentTag = "OpenRecent";
    private const string RecentProjectTagPrefix = "RecentProject_";
    private const string ClearRecentProjectsTag = "ClearRecentProjects";
    private const string ReloadProjectTag = "ReloadProject";
    private const string CloseProjectTag = "CloseProject";
    private const string ExitTag = "Exit";

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
            Icon = new SymbolIcon(Symbol.GlobalNavigationButton)
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
            icon: new SymbolIcon(Symbol.NewFolder),
            label: _stringLocalizer.GetString("MainMenu_NewProject"),
            tooltip: _stringLocalizer.GetString("MainMenu_NewProjectTooltip"),
            isEnabled: true);
        _menuNavItem.MenuItems.Add(newProjectNavItem);

        // Open Project
        var openProjectNavItem = CreateMenuItem(
            tag: OpenProjectTag,
            icon: new SymbolIcon(Symbol.OpenLocal),
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
            icon: new SymbolIcon(Symbol.Refresh),
            label: _stringLocalizer.GetString("MainMenu_ReloadProject"),
            tooltip: _stringLocalizer.GetString("MainMenu_ReloadProjectTooltip"),
            isEnabled: isWorkspaceLoaded);
        _menuNavItem.MenuItems.Add(reloadProjectNavItem);

        // Close Project
        var closeProjectNavItem = CreateMenuItem(
            tag: CloseProjectTag,
            icon: new SymbolIcon(Symbol.Cancel),
            label: _stringLocalizer.GetString("MainMenu_CloseProject"),
            tooltip: _stringLocalizer.GetString("MainMenu_CloseProjectTooltip"),
            isEnabled: isWorkspaceLoaded);
        _menuNavItem.MenuItems.Add(closeProjectNavItem);

        _menuNavItem.MenuItems.Add(new NavigationViewItemSeparator());

        // Settings
        var settingsNavItem = CreateMenuItem(
            tag: NavigationConstants.SettingsTag,
            icon: new SymbolIcon(Symbol.Setting),
            label: _stringLocalizer.GetString("MainMenu_Settings"),
            tooltip: _stringLocalizer.GetString("MainMenu_SettingsTooltip"),
            isEnabled: true);
        _menuNavItem.MenuItems.Add(settingsNavItem);

        _menuNavItem.MenuItems.Add(new NavigationViewItemSeparator());



        // Exit
        var exitNavItem = CreateMenuItem(
            tag: ExitTag,
            icon: new FontIcon 
            { 
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                Glyph = "\uE7E8"
            },
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
            Icon = new SymbolIcon(Symbol.Clock),
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
                Icon = new SymbolIcon(Symbol.Delete),
                Content = _stringLocalizer.GetString("MainMenu_ClearRecentProjects")
            };
            clearRecentNavItem.SetValue(NavigationViewItem.SelectsOnInvokedProperty, false);

            ToolTipService.SetToolTip(clearRecentNavItem, _stringLocalizer.GetString("MainMenu_ClearRecentProjectsTooltip"));
            ToolTipService.SetPlacement(clearRecentNavItem, PlacementMode.Right);

            openRecentNavItem.MenuItems.Add(clearRecentNavItem);
        }

        return openRecentNavItem;
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
    public void HandleItemInvoked(NavigationViewItem invokedItem)
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
            ViewModel.OpenRecentProject(projectFilePath);
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
