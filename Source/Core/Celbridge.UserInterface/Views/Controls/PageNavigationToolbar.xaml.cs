using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;
using Microsoft.UI.Xaml.Media.Animation;

namespace Celbridge.UserInterface.Views;

public sealed partial class PageNavigationToolbar : UserControl
{
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private MainMenu? _mainMenu;
    private bool _hasShortcuts;
    private ShortcutMenuBuilder? _shortcutMenuBuilder;

    public PageNavigationToolbarViewModel ViewModel { get; }

    /// <summary>
    /// Builds shortcut buttons from the given definitions and wires up click handling.
    /// </summary>
    public bool BuildShortcutButtons(IReadOnlyList<Shortcut> shortcuts, Action<string> onScriptExecute)
    {
        ClearShortcutButtons();

        var logger = ServiceLocator.AcquireService<Logging.ILogger<ShortcutMenuBuilder>>();
        _shortcutMenuBuilder = new ShortcutMenuBuilder(logger);
        _shortcutMenuBuilder.ShortcutClicked += (tag) =>
        {
            if (_shortcutMenuBuilder.TryGetScript(tag, out var script) && !string.IsNullOrEmpty(script))
            {
                onScriptExecute(script);
            }
        };

        var hasShortcuts = _shortcutMenuBuilder.BuildShortcutButtons(shortcuts, ShortcutButtonsPanel);
        SetShortcutButtonsVisible(hasShortcuts);

        return hasShortcuts;
    }

    /// <summary>
    /// Clears all shortcut buttons and disposes the builder.
    /// </summary>
    public void ClearShortcutButtons()
    {
        _shortcutMenuBuilder = null;
        ShortcutButtonsPanel.Children.Clear();
        SetShortcutButtonsVisible(false);
    }

    /// <summary>
    /// Sets whether shortcut buttons are populated and shows/hides them accordingly.
    /// Shortcuts are only visible when populated and the workspace page is active.
    /// </summary>
    public void SetShortcutButtonsVisible(bool isVisible)
    {
        _hasShortcuts = isVisible;
        UpdateShortcutButtonsVisibility(animate: isVisible);
    }

    private void UpdateShortcutButtonsVisibility(bool animate = false)
    {
        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        var shouldShow = _hasShortcuts && userInterfaceService.ActivePage == ApplicationPage.Workspace;

        if (shouldShow)
        {
            if (animate)
            {
                ShortcutButtonsContainer.Opacity = 0;
                ShortcutButtonsContainer.Visibility = Visibility.Visible;

                var storyboard = new Storyboard();
                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = new Duration(TimeSpan.FromMilliseconds(500)),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                Storyboard.SetTarget(fadeIn, ShortcutButtonsContainer);
                Storyboard.SetTargetProperty(fadeIn, "Opacity");
                storyboard.Children.Add(fadeIn);
                storyboard.Begin();
            }
            else
            {
                ShortcutButtonsContainer.Opacity = 1;
                ShortcutButtonsContainer.Visibility = Visibility.Visible;
            }
        }
        else
        {
            ShortcutButtonsContainer.Visibility = Visibility.Collapsed;
            ShortcutButtonsContainer.Opacity = 1;
        }
    }

    public PageNavigationToolbar()
    {
        this.InitializeComponent();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<PageNavigationToolbarViewModel>();

        this.DataContext = ViewModel;

        Loaded += OnPageNavigationToolbar_Loaded;
        Unloaded += OnPageNavigationToolbar_Unloaded;
    }

    private void OnPageNavigationToolbar_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnLoaded();

        _mainMenu = new MainMenu();
        _mainMenu.OnLoaded();
        _mainMenu.MenuItemInvoked += OnMainMenu_ItemInvoked;
        PageNavigation.MenuItems.Insert(0, _mainMenu.GetMenuNavItem());

        ApplyTooltips();

        _messengerService.Register<ActivePageChangedMessage>(this, OnActivePageChanged);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
    }

    private void OnPageNavigationToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();

        if (_mainMenu != null)
        {
            _mainMenu.MenuItemInvoked -= OnMainMenu_ItemInvoked;
            _mainMenu.OnUnloaded();
        }

        Loaded -= OnPageNavigationToolbar_Loaded;
        Unloaded -= OnPageNavigationToolbar_Unloaded;

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

        UpdateWorkspaceTooltip();
    }

    private void UpdateWorkspaceTooltip()
    {
        var tooltip = !string.IsNullOrEmpty(ViewModel.ProjectFilePath)
            ? ViewModel.ProjectFilePath
            : _stringLocalizer.GetString("TitleBar_WorkspaceTooltip");

        ToolTipService.SetToolTip(WorkspaceNavItem, tooltip);
        ToolTipService.SetPlacement(WorkspaceNavItem, PlacementMode.Bottom);
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        UpdateNavigationSelection(ApplicationPage.Workspace);
        UpdateWorkspaceTooltip();
    }

    private void OnActivePageChanged(object recipient, ActivePageChangedMessage message)
    {
        UpdateNavigationSelection(message.ActivePage);

        var isNavigatingToWorkspace = message.ActivePage == ApplicationPage.Workspace;
        UpdateShortcutButtonsVisibility(animate: isNavigatingToWorkspace);
    }

    private void UpdateNavigationSelection(ApplicationPage activePage)
    {
        PageNavigation.SelectionChanged -= PageNavigation_SelectionChanged;

        try
        {
            switch (activePage)
            {
                case ApplicationPage.Home:
                    PageNavigation.SelectedItem = HomeNavItem;
                    break;
                case ApplicationPage.Community:
                    PageNavigation.SelectedItem = CommunityNavItem;
                    break;
                case ApplicationPage.Workspace:
                    PageNavigation.SelectedItem = WorkspaceNavItem;
                    break;
                case ApplicationPage.Settings:
                    PageNavigation.SelectedItem = null;
                    break;
                default:
                    PageNavigation.SelectedItem = null;
                    break;
            }
        }
        finally
        {
            PageNavigation.SelectionChanged += PageNavigation_SelectionChanged;
        }
    }

    private void PageNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
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

    private void PageNavigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem invokedItem)
        {
            _mainMenu?.HandleItemInvoked(invokedItem);
        }
    }

    private void OnMainMenu_ItemInvoked(object? sender, EventArgs e)
    {
        CloseFlyoutMenus();
    }

    private void CloseFlyoutMenus()
    {
        CloseFlyoutMenusRecursive(PageNavigation.MenuItems);
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
}
