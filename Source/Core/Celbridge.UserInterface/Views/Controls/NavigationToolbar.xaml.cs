using Celbridge.Navigation;
using Celbridge.Platform;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views;

public sealed partial class NavigationToolbar : UserControl
{
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private MainMenu? _mainMenu;

    public NavigationToolbarViewModel ViewModel { get; }

    public NavigationToolbar()
    {
        this.InitializeComponent();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<NavigationToolbarViewModel>();

        this.DataContext = ViewModel;

        // The menu opens over the document area, where a hosted web view would take the click too.
        var overlayInputSuppressor = ServiceLocator.AcquireService<IOverlayInputSuppressor>();
        overlayInputSuppressor.SuppressWhileOpen(MainMenuFlyout);

        Loaded += OnNavigationToolbar_Loaded;
        Unloaded += OnNavigationToolbar_Unloaded;
    }

    private void OnNavigationToolbar_Loaded(object sender, RoutedEventArgs e)
    {
        // macOS surfaces these commands through the native menubar, so the in-window hamburger menu is
        // shown only on platforms without one (Windows, Linux).
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

    private void OnNavigationToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        _mainMenu?.OnUnloaded();

        Loaded -= OnNavigationToolbar_Loaded;
        Unloaded -= OnNavigationToolbar_Unloaded;

        _messengerService.UnregisterAll(this);
    }

    /// <summary>
    /// The controls in this toolbar that a user can click.
    /// </summary>
    internal IReadOnlyList<FrameworkElement> GetInteractiveElements()
    {
        var elements = new List<FrameworkElement>
        {
            MainMenuButton,
            HomeNavItem,
            ProjectSwitcher,
            ProjectHealthButton
        };

        return elements;
    }

    private void ApplyTooltips()
    {
        var mainMenuTooltip = _stringLocalizer.GetString("TitleBar_MainMenuTooltip");
        ToolTipService.SetToolTip(MainMenuButton, mainMenuTooltip);
        ToolTipService.SetPlacement(MainMenuButton, PlacementMode.Bottom);
        AutomationProperties.SetName(MainMenuButton, mainMenuTooltip);

        // Home carries only an icon in its Content, so give assistive technology an explicit name.
        var homeTooltip = _stringLocalizer.GetString("TitleBar_HomeTooltip");
        ToolTipService.SetToolTip(HomeNavItem, homeTooltip);
        ToolTipService.SetPlacement(HomeNavItem, PlacementMode.Bottom);
        AutomationProperties.SetName(HomeNavItem, homeTooltip);
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        UpdateNavigationSelection(ApplicationPage.Workspace);
    }

    private void OnActivePageChanged(object recipient, ActivePageChangedMessage message)
    {
        UpdateNavigationSelection(message.ActivePage);
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
                case ApplicationPage.Workspace:
                    // The project switcher is custom content, not a nav item, so no menu item is selected here.
                    PageNavigation.SelectedItem = null;
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
