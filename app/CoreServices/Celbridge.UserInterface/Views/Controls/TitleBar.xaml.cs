using Celbridge.Navigation;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views;

public sealed partial class TitleBar : UserControl
{
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private Window? _mainWindow;
    private MainMenu? _mainMenu;

    public TitleBarViewModel ViewModel { get; }

    public TitleBar()
    {
        this.InitializeComponent();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<TitleBarViewModel>();

        this.DataContext = ViewModel;

        Loaded += OnTitleBar_Loaded;
        Unloaded += OnTitleBar_Unloaded;
    }

    private void OnTitleBar_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnLoaded();

        // Create and add the MainMenu control
        _mainMenu = new MainMenu();
        _mainMenu.OnLoaded();
        _mainMenu.MenuItemInvoked += OnMainMenu_ItemInvoked;
        TitleBarNavigation.MenuItems.Insert(0, _mainMenu.GetMenuNavItem());

        ApplyTooltips();

        // Register for window mode changes
        _messengerService.Register<MainWindowActivatedMessage>(this, OnMainWindowActivated);
        _messengerService.Register<MainWindowDeactivatedMessage>(this, OnMainWindowDeactivated);
        _messengerService.Register<ActivePageChangedMessage>(this, OnActivePageChanged);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);

        // Listen to ViewModel property changes to update interactive regions
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Update interactive regions when toolbar size changes
        LayoutToolbar.SizeChanged += OnLayoutToolbar_SizeChanged;
        TitleBarNavigation.SizeChanged += OnTitleBarNavigation_SizeChanged;

        // Cache the main window reference
        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _mainWindow = userInterfaceService.MainWindow as Window;

        // Initial update of interactive regions after layout
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateInteractiveRegions();
        });
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        UpdateNavigationSelection(ApplicationPage.Workspace);
    }

    private void OnTitleBar_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();

        // Unregister all event handlers to avoid memory leaks
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        LayoutToolbar.SizeChanged -= OnLayoutToolbar_SizeChanged;
        TitleBarNavigation.SizeChanged -= OnTitleBarNavigation_SizeChanged;

        if (_mainMenu != null)
        {
            _mainMenu.MenuItemInvoked -= OnMainMenu_ItemInvoked;
            _mainMenu.OnUnloaded();
        }

        Loaded -= OnTitleBar_Loaded;
        Unloaded -= OnTitleBar_Unloaded;

        _messengerService.UnregisterAll(this);
    }

    private void ApplyTooltips()
    {
        var homeTooltip = _stringLocalizer.GetString("TitleBar_HomeTooltip");
        ToolTipService.SetToolTip(HomeNavItem, homeTooltip);
        ToolTipService.SetPlacement(HomeNavItem, PlacementMode.Bottom);

        var communityTooltip = _stringLocalizer.GetString("TitleBar_CommunityTooltip");
        ToolTipService.SetToolTip(CommunityNavItem, communityTooltip);
        ToolTipService.SetPlacement(CommunityNavItem, PlacementMode.Bottom);

        var settingsTooltip = _stringLocalizer.GetString("TitleBar_SettingsTooltip");
        ToolTipService.SetToolTip(SettingsNavItem, settingsTooltip);
        ToolTipService.SetPlacement(SettingsNavItem, PlacementMode.Bottom);

        var workspaceTooltip = _stringLocalizer.GetString("TitleBar_WorkspaceTooltip");
        ToolTipService.SetToolTip(WorkspaceNavItem, workspaceTooltip);
        ToolTipService.SetPlacement(WorkspaceNavItem, PlacementMode.Bottom);
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsWorkspaceActive))
        {
            // Update interactive regions when workspace activation state changes
            UpdateInteractiveRegions();
        }
    }

    private void OnMainWindowActivated(object recipient, MainWindowActivatedMessage message)
    {
        VisualStateManager.GoToState(this, "Active", false);
    }

    private void OnMainWindowDeactivated(object recipient, MainWindowDeactivatedMessage message)
    {
        VisualStateManager.GoToState(this, "Inactive", false);
    }

    private void OnActivePageChanged(object recipient, ActivePageChangedMessage message)
    {
        // Update the navigation selection to reflect the current page
        UpdateNavigationSelection(message.ActivePage);
    }

    private void UpdateNavigationSelection(ApplicationPage activePage)
    {
        // Temporarily unhook the selection changed event to avoid re-triggering navigation
        TitleBarNavigation.SelectionChanged -= TitleBarNavigation_SelectionChanged;

        try
        {
            switch (activePage)
            {
                case ApplicationPage.Workspace:
                    TitleBarNavigation.SelectedItem = WorkspaceNavItem;
                    break;
                case ApplicationPage.Community:
                    TitleBarNavigation.SelectedItem = CommunityNavItem;
                    break;
                case ApplicationPage.Settings:
                    TitleBarNavigation.SelectedItem = SettingsNavItem;
                    break;
                case ApplicationPage.Home:
                    TitleBarNavigation.SelectedItem = HomeNavItem;
                    break;
                default:
                    // Clear selection for unknown pages
                    TitleBarNavigation.SelectedItem = null;
                    break;
            }
        }
        finally
        {
            TitleBarNavigation.SelectionChanged += TitleBarNavigation_SelectionChanged;
        }
    }

    private void OnLayoutToolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Update interactive regions whenever the toolbar size changes
        if (e.NewSize.Width > 0)
        {
            UpdateInteractiveRegions();
        }
    }

    private void OnTitleBarNavigation_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Update interactive regions whenever the navigation size changes
        if (e.NewSize.Width > 0)
        {
            UpdateInteractiveRegions();
        }
    }

    private void TitleBarNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem selectedItem)
        {
            var tag = selectedItem.Tag?.ToString();
            if (string.IsNullOrEmpty(tag))
            {
                return;
            }

            ViewModel.NavigateToPage(tag);
        }
    }

    private void TitleBarNavigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        // Delegate menu item handling to the MainMenu control
        if (args.InvokedItemContainer is NavigationViewItem invokedItem)
        {
            _mainMenu?.HandleItemInvoked(invokedItem);
        }
    }

    private void OnMainMenu_ItemInvoked(object? sender, EventArgs e)
    {
        // Close all flyout menus when a menu item is invoked
        CloseFlyoutMenus();
    }

    private void CloseFlyoutMenus()
    {
        // Close all flyout menus by recursively collapsing expanded items
        CloseFlyoutMenusRecursive(TitleBarNavigation.MenuItems);
    }

    private void CloseFlyoutMenusRecursive(IList<object> menuItems)
    {
        foreach (var item in menuItems)
        {
            if (item is NavigationViewItem navItem)
            {
                if (navItem.MenuItems.Count > 0)
                {
                    navItem.IsExpanded = false;
                    CloseFlyoutMenusRecursive(navItem.MenuItems);
                }
            }
        }
    }

    private void UpdateInteractiveRegions()
    {
#if WINDOWS
        // For Windows, we need to set the input non-client pointer source to allow
        // interactivity with the navigation and toolbar in the title bar area.
        try
        {
            if (_mainWindow == null)
            {
                return;
            }

            var appWindow = _mainWindow.AppWindow;
            if (appWindow == null)
            {
                return;
            }

            var nonClientInputSrc = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(appWindow.Id);
            var scale = _mainWindow.Content.XamlRoot?.RasterizationScale ?? 1.0;

            var regions = new List<Windows.Graphics.RectInt32>();

            // Add passthrough region for the TitleBar navigation
            if (TitleBarNavigation.ActualWidth > 0)
            {
                var navTransform = TitleBarNavigation.TransformToVisual(_mainWindow.Content);
                var navPosition = navTransform.TransformPoint(new Windows.Foundation.Point(0, 0));

                regions.Add(new Windows.Graphics.RectInt32(
                    (int)(navPosition.X * scale),
                    (int)(navPosition.Y * scale),
                    (int)(TitleBarNavigation.ActualWidth * scale),
                    (int)(TitleBarNavigation.ActualHeight * scale)
                ));
            }

            // Add passthrough region for the layout toolbar if workspace is active
            if (ViewModel.IsWorkspaceActive && LayoutToolbar.ActualWidth > 0)
            {
                var toolbarTransform = LayoutToolbar.TransformToVisual(_mainWindow.Content);
                var toolbarPosition = toolbarTransform.TransformPoint(new Windows.Foundation.Point(0, 0));

                regions.Add(new Windows.Graphics.RectInt32(
                    (int)(toolbarPosition.X * scale),
                    (int)(toolbarPosition.Y * scale),
                    (int)(LayoutToolbar.ActualWidth * scale),
                    (int)(LayoutToolbar.ActualHeight * scale)
                ));
            }

            if (regions.Count > 0)
            {
                nonClientInputSrc.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough, regions.ToArray());
            }
            else
            {
                nonClientInputSrc.ClearRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough);
            }
        }
        catch
        {
            // Silently ignore any errors
        }
#endif
    }

    /// <summary>
    /// Call this method after the toolbar becomes visible and has been laid out
    /// to update the interactive regions for the title bar. This prevents double 
    /// clicks on the panel toggles registering as double clicks on the title bar.
    /// </summary>
    public void RefreshInteractiveRegions()
    {
        UpdateInteractiveRegions();
    }
}
