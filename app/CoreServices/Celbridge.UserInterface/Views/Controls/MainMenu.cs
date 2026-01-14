using Celbridge.UserInterface.ViewModels.Controls;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// Helper class that creates and manages the MainMenu NavigationViewItem.
/// </summary>
public class MainMenu
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly NavigationViewItem _menuNavItem;
    private readonly NavigationViewItem _newProjectNavItem;
    private readonly NavigationViewItem _openProjectNavItem;
    private readonly NavigationViewItem _reloadProjectNavItem;
    private readonly NavigationViewItem _closeProjectNavItem;
    private readonly NavigationViewItem _exitNavItem;

    public MainMenuViewModel ViewModel { get; }

    /// <summary>
    /// Event raised when a menu item is invoked. The parent control should handle closing flyouts.
    /// </summary>
    public event EventHandler? MenuItemInvoked;

    public MainMenu()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<MainMenuViewModel>();

        // Create the menu items programmatically
        _newProjectNavItem = new NavigationViewItem
        {
            Tag = "NewProject",
            Icon = new SymbolIcon(Symbol.NewFolder)
        };
        _newProjectNavItem.SetValue(NavigationViewItem.SelectsOnInvokedProperty, false);

        _openProjectNavItem = new NavigationViewItem
        {
            Tag = "OpenProject",
            Icon = new SymbolIcon(Symbol.OpenLocal)
        };
        _openProjectNavItem.SetValue(NavigationViewItem.SelectsOnInvokedProperty, false);

        _reloadProjectNavItem = new NavigationViewItem
        {
            Tag = "ReloadProject",
            Icon = new SymbolIcon(Symbol.Refresh)
        };
        _reloadProjectNavItem.SetValue(NavigationViewItem.SelectsOnInvokedProperty, false);

        _closeProjectNavItem = new NavigationViewItem
        {
            Tag = "CloseProject",
            Icon = new SymbolIcon(Symbol.Cancel)
        };
        _closeProjectNavItem.SetValue(NavigationViewItem.SelectsOnInvokedProperty, false);

        _exitNavItem = new NavigationViewItem
        {
            Tag = "Exit",
            Icon = new FontIcon { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"), Glyph = "\uE7E8" }
        };
        _exitNavItem.SetValue(NavigationViewItem.SelectsOnInvokedProperty, false);

        _menuNavItem = new NavigationViewItem
        {
            Tag = "Menu",
            Icon = new SymbolIcon(Symbol.GlobalNavigationButton)
        };
        _menuNavItem.SetValue(NavigationViewItem.SelectsOnInvokedProperty, false);

        // Add menu items
        _menuNavItem.MenuItems.Add(_newProjectNavItem);
        _menuNavItem.MenuItems.Add(_openProjectNavItem);
        _menuNavItem.MenuItems.Add(_reloadProjectNavItem);
        _menuNavItem.MenuItems.Add(_closeProjectNavItem);
        _menuNavItem.MenuItems.Add(new NavigationViewItemSeparator());
        _menuNavItem.MenuItems.Add(_exitNavItem);

        ApplyLabels();
        ApplyTooltips();

        // Bind IsEnabled for reload/close to IsWorkspaceLoaded
        ViewModel.PropertyChanged += OnViewModel_PropertyChanged;
        UpdateWorkspaceLoadedState();
    }

    private void OnViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsWorkspaceLoaded))
        {
            UpdateWorkspaceLoadedState();
        }
    }

    private void UpdateWorkspaceLoadedState()
    {
        _reloadProjectNavItem.IsEnabled = ViewModel.IsWorkspaceLoaded;
        _closeProjectNavItem.IsEnabled = ViewModel.IsWorkspaceLoaded;
    }

    public void OnLoaded()
    {
        ViewModel.OnLoaded();
    }

    public void OnUnloaded()
    {
        ViewModel.OnUnloaded();
        ViewModel.PropertyChanged -= OnViewModel_PropertyChanged;
    }

    private void ApplyTooltips()
    {
        var menuTooltip = _stringLocalizer.GetString("TitleBar_MainMenuTooltip");
        ToolTipService.SetToolTip(_menuNavItem, menuTooltip);
        ToolTipService.SetPlacement(_menuNavItem, PlacementMode.Bottom);

        var newProjectTooltip = _stringLocalizer.GetString("MainMenu_NewProjectTooltip");
        ToolTipService.SetToolTip(_newProjectNavItem, newProjectTooltip);
        ToolTipService.SetPlacement(_newProjectNavItem, PlacementMode.Right);

        var openProjectTooltip = _stringLocalizer.GetString("MainMenu_OpenProjectTooltip");
        ToolTipService.SetToolTip(_openProjectNavItem, openProjectTooltip);
        ToolTipService.SetPlacement(_openProjectNavItem, PlacementMode.Right);

        var reloadProjectTooltip = _stringLocalizer.GetString("MainMenu_ReloadProjectTooltip");
        ToolTipService.SetToolTip(_reloadProjectNavItem, reloadProjectTooltip);
        ToolTipService.SetPlacement(_reloadProjectNavItem, PlacementMode.Right);

        var closeProjectTooltip = _stringLocalizer.GetString("MainMenu_CloseProjectTooltip");
        ToolTipService.SetToolTip(_closeProjectNavItem, closeProjectTooltip);
        ToolTipService.SetPlacement(_closeProjectNavItem, PlacementMode.Right);

        var exitTooltip = _stringLocalizer.GetString("MainMenu_ExitTooltip");
        ToolTipService.SetToolTip(_exitNavItem, exitTooltip);
        ToolTipService.SetPlacement(_exitNavItem, PlacementMode.Right);
    }

    private void ApplyLabels()
    {
        _newProjectNavItem.Content = _stringLocalizer.GetString("MainMenu_NewProject");
        _openProjectNavItem.Content = _stringLocalizer.GetString("MainMenu_OpenProject");
        _reloadProjectNavItem.Content = _stringLocalizer.GetString("MainMenu_ReloadProject");
        _closeProjectNavItem.Content = _stringLocalizer.GetString("MainMenu_CloseProject");
        _exitNavItem.Content = _stringLocalizer.GetString("MainMenu_Exit");
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
