using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// Helper class that creates and manages the MainMenu NavigationViewItem.
/// </summary>
public class MainMenu
{
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
            Tag = "Menu",
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
            tag: "NewProject",
            icon: new SymbolIcon(Symbol.NewFolder),
            label: _stringLocalizer.GetString("MainMenu_NewProject"),
            tooltip: _stringLocalizer.GetString("MainMenu_NewProjectTooltip"),
            isEnabled: true);
        _menuNavItem.MenuItems.Add(newProjectNavItem);

        // Open Project
        var openProjectNavItem = CreateMenuItem(
            tag: "OpenProject",
            icon: new SymbolIcon(Symbol.OpenLocal),
            label: _stringLocalizer.GetString("MainMenu_OpenProject"),
            tooltip: _stringLocalizer.GetString("MainMenu_OpenProjectTooltip"),
            isEnabled: true);
        _menuNavItem.MenuItems.Add(openProjectNavItem);

        // Reload Project
        var reloadProjectNavItem = CreateMenuItem(
            tag: "ReloadProject",
            icon: new SymbolIcon(Symbol.Refresh),
            label: _stringLocalizer.GetString("MainMenu_ReloadProject"),
            tooltip: _stringLocalizer.GetString("MainMenu_ReloadProjectTooltip"),
            isEnabled: isWorkspaceLoaded);
        _menuNavItem.MenuItems.Add(reloadProjectNavItem);

        // Close Project
        var closeProjectNavItem = CreateMenuItem(
            tag: "CloseProject",
            icon: new SymbolIcon(Symbol.Cancel),
            label: _stringLocalizer.GetString("MainMenu_CloseProject"),
            tooltip: _stringLocalizer.GetString("MainMenu_CloseProjectTooltip"),
            isEnabled: isWorkspaceLoaded);
        _menuNavItem.MenuItems.Add(closeProjectNavItem);

        _menuNavItem.MenuItems.Add(new NavigationViewItemSeparator());

        // Settings
        var settingsNavItem = CreateMenuItem(
            tag: "Settings",
            icon: new SymbolIcon(Symbol.Setting),
            label: _stringLocalizer.GetString("MainMenu_Settings"),
            tooltip: _stringLocalizer.GetString("MainMenu_SettingsTooltip"),
            isEnabled: true);
        _menuNavItem.MenuItems.Add(settingsNavItem);

        _menuNavItem.MenuItems.Add(new NavigationViewItemSeparator());

        // Exit
        var exitIcon = new FontIcon 
        { 
            FontFamily = new FontFamily("Segoe MDL2 Assets"), 
            Glyph = "\uE7E8" 
        };

        var exitNavItem = CreateMenuItem(
            tag: "Exit",
            icon: exitIcon,
            label: _stringLocalizer.GetString("MainMenu_Exit"),
            tooltip: _stringLocalizer.GetString("MainMenu_ExitTooltip"),
            isEnabled: true);
        _menuNavItem.MenuItems.Add(exitNavItem);
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

        switch (tag)
        {
            case "Menu":
                // Menu item just opens its flyout, no action needed
                break;

            case "NewProject":
                ViewModel.NewProject();
                MenuItemInvoked?.Invoke(this, EventArgs.Empty);
                break;

            case "OpenProject":
                ViewModel.OpenProject();
                MenuItemInvoked?.Invoke(this, EventArgs.Empty);
                break;

            case "ReloadProject":
                _ = ViewModel.ReloadProjectAsync();
                MenuItemInvoked?.Invoke(this, EventArgs.Empty);
                break;

            case "CloseProject":
                _ = ViewModel.CloseProjectAsync();
                MenuItemInvoked?.Invoke(this, EventArgs.Empty);
                break;

            case "Settings":
                ViewModel.NavigateToSettings();
                MenuItemInvoked?.Invoke(this, EventArgs.Empty);
                break;

            case "Exit":
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
