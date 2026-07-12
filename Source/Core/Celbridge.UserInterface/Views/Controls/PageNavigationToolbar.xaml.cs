using Celbridge.Platform;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.UserInterface.Views.Controls;
using Celbridge.Workspace;
using Microsoft.UI.Xaml.Media.Animation;

namespace Celbridge.UserInterface.Views;

public sealed partial class PageNavigationToolbar : UserControl
{
    // Matches the app's standard title-bar / nav-bar icon size, so utility launcher glyphs sit level
    // with the project button and the Home and Community icons.
    private const double TitleBarIconGlyphSize = 16;

    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private MainMenu? _mainMenu;
    private bool _hasShortcuts;
    private bool _hasUtilities;
    private ShortcutMenuBuilder? _shortcutMenuBuilder;
    private Action<string>? _onOpenUtility;

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
    /// Builds the utility launcher buttons, placed to the right of the project button and to the left of
    /// the shortcut buttons. Each button opens or activates its utility via the callback. Returns true
    /// when at least one button was built.
    /// </summary>
    public bool BuildUtilityButtons(IReadOnlyList<UtilityButton> utilities, Action<string> onOpenUtility)
    {
        ClearUtilityButtons();

        _onOpenUtility = onOpenUtility;

        foreach (var utility in utilities)
        {
            var button = new ShortcutButton();
            button.SetIcon(utility.Icon);

            // Match the glyph to the surrounding title-bar icons (project button, Home, Community) rather
            // than the larger default shortcut-button glyph.
            button.SetIconSize(TitleBarIconGlyphSize);

            button.SetTooltip(utility.Tooltip);
            button.SetAutomationName(utility.Tooltip);
            button.Tag = utility.UtilityId;
            button.Click += OnUtilityButton_Click;

            UtilityButtonsPanel.Children.Add(button);
        }

        _hasUtilities = utilities.Count > 0;
        UpdateSeparators();
        UpdatePaneButtonsVisibility();

        return _hasUtilities;
    }

    /// <summary>
    /// Clears all utility launcher buttons.
    /// </summary>
    public void ClearUtilityButtons()
    {
        UtilityButtonsPanel.Children.Clear();
        _onOpenUtility = null;
        _hasUtilities = false;
        UpdateSeparators();
        UpdatePaneButtonsVisibility();
    }

    private void OnUtilityButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is ShortcutButton button
            && button.Tag is string utilityId)
        {
            _onOpenUtility?.Invoke(utilityId);
        }
    }

    /// <summary>
    /// Sets whether shortcut buttons are populated and shows/hides them accordingly.
    /// Shortcuts are only visible when populated and the workspace page is active.
    /// </summary>
    public void SetShortcutButtonsVisible(bool isVisible)
    {
        _hasShortcuts = isVisible;
        UpdateSeparators();
        UpdatePaneButtonsVisibility(animate: isVisible);
    }

    // The utility group owns both its separators. The utility trailing separator already divides
    // utilities from the shortcuts, so the shortcuts add their own leading separator only when no
    // utilities precede them.
    private void UpdateSeparators()
    {
        UtilityLeadingSeparator.Visibility = _hasUtilities ? Visibility.Visible : Visibility.Collapsed;
        UtilityTrailingSeparator.Visibility = _hasUtilities ? Visibility.Visible : Visibility.Collapsed;

        var showShortcutSeparator = _hasShortcuts && !_hasUtilities;
        ShortcutLeadingSeparator.Visibility = showShortcutSeparator ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePaneButtonsVisibility(bool animate = false)
    {
        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        var hasButtons = _hasShortcuts || _hasUtilities;
        var shouldShow = hasButtons && userInterfaceService.ActivePage == ApplicationPage.Workspace;

        if (shouldShow)
        {
            if (animate)
            {
                PaneButtonsContainer.Opacity = 0;
                PaneButtonsContainer.Visibility = Visibility.Visible;

                var storyboard = new Storyboard();
                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = new Duration(TimeSpan.FromMilliseconds(500)),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                Storyboard.SetTarget(fadeIn, PaneButtonsContainer);
                Storyboard.SetTargetProperty(fadeIn, "Opacity");
                storyboard.Children.Add(fadeIn);
                storyboard.Begin();
            }
            else
            {
                PaneButtonsContainer.Opacity = 1;
                PaneButtonsContainer.Visibility = Visibility.Visible;
            }
        }
        else
        {
            PaneButtonsContainer.Visibility = Visibility.Collapsed;
            PaneButtonsContainer.Opacity = 1;
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

        // macOS surfaces these commands through the native menubar (see MacOSMainMenu), so the in-window
        // hamburger menu is mounted only on platforms without one (Windows, Linux).
        var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        if (!platformInfo.UsesNativeMenuBar)
        {
            _mainMenu = new MainMenu();
            _mainMenu.OnLoaded();
            _mainMenu.MenuItemInvoked += OnMainMenu_ItemInvoked;
            PageNavigation.MenuItems.Insert(0, _mainMenu.GetMenuNavItem());
        }

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
        UpdatePaneButtonsVisibility(animate: isNavigatingToWorkspace);
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
