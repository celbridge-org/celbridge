using Celbridge.Platform;
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
        UpdateSeparators();
        UpdatePaneButtonsVisibility(animate: isVisible);
    }

    // The shortcut group shows its leading separator (dividing it from the project button) whenever
    // shortcuts are present.
    private void UpdateSeparators()
    {
        ShortcutLeadingSeparator.Visibility = _hasShortcuts ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePaneButtonsVisibility(bool animate = false)
    {
        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        var shouldShow = _hasShortcuts && userInterfaceService.ActivePage == ApplicationPage.Workspace;

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
        // hamburger menu is shown only on platforms without one (Windows, Linux).
        var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        if (!platformInfo.UsesNativeMenuBar)
        {
            _mainMenu = new MainMenu(MainMenuFlyout);
            _mainMenu.OnLoaded();
            MainMenuButton.Visibility = Visibility.Visible;
        }

        ApplyTooltips();

        _messengerService.Register<ActivePageChangedMessage>(this, OnActivePageChanged);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
    }

    private void OnPageNavigationToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();

        _mainMenu?.OnUnloaded();

        Loaded -= OnPageNavigationToolbar_Loaded;
        Unloaded -= OnPageNavigationToolbar_Unloaded;

        _messengerService.UnregisterAll(this);
    }

    private void ApplyTooltips()
    {
        var mainMenuTooltip = _stringLocalizer.GetString("TitleBar_MainMenuTooltip");
        ToolTipService.SetToolTip(MainMenuButton, mainMenuTooltip);
        ToolTipService.SetPlacement(MainMenuButton, PlacementMode.Bottom);
        AutomationProperties.SetName(MainMenuButton, mainMenuTooltip);

        // Home and Community carry only an icon in their Content, so give assistive technology an explicit name.
        var homeTooltip = _stringLocalizer.GetString("TitleBar_HomeTooltip");
        ToolTipService.SetToolTip(HomeNavItem, homeTooltip);
        ToolTipService.SetPlacement(HomeNavItem, PlacementMode.Bottom);
        AutomationProperties.SetName(HomeNavItem, homeTooltip);

        var communityTooltip = _stringLocalizer.GetString("TitleBar_CommunityTooltip");
        ToolTipService.SetToolTip(CommunityNavItem, communityTooltip);
        ToolTipService.SetPlacement(CommunityNavItem, PlacementMode.Bottom);
        AutomationProperties.SetName(CommunityNavItem, communityTooltip);

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
}
