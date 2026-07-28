using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Platform;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views;

public sealed partial class PageNavigationToolbar : UserControl
{
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private MainMenu? _mainMenu;
    private MainMenuViewModel? _recentProjectsViewModel;

    public PageNavigationToolbarViewModel ViewModel { get; }

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

        // The Workspace button's switcher reuses the main menu view model for the recent-projects list and open
        // logic. It is available on every platform, not only those without a native menu bar.
        _recentProjectsViewModel = ServiceLocator.AcquireService<MainMenuViewModel>();
        RecentProjectsFlyout.Opening += OnRecentProjectsFlyoutOpening;

        ApplyTooltips();

        _messengerService.Register<ActivePageChangedMessage>(this, OnActivePageChanged);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
    }

    private void OnPageNavigationToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();

        _mainMenu?.OnUnloaded();

        RecentProjectsFlyout.Opening -= OnRecentProjectsFlyoutOpening;

        Loaded -= OnPageNavigationToolbar_Loaded;
        Unloaded -= OnPageNavigationToolbar_Unloaded;

        _messengerService.UnregisterAll(this);
    }

    private void OnRecentProjectsFlyoutOpening(object? sender, object e)
    {
        RecentProjectsFlyout.Items.Clear();

        var viewModel = _recentProjectsViewModel;
        var recentProjects = viewModel?.GetRecentProjects();
        if (viewModel is null ||
            recentProjects is null ||
            recentProjects.Count == 0)
        {
            var emptyItem = new MenuFlyoutItem
            {
                Text = _stringLocalizer.GetString("Menu_NoRecentProjects"),
                IsEnabled = false
            };
            RecentProjectsFlyout.Items.Add(emptyItem);
            return;
        }

        // The switcher is opened from a bare chevron, so a disabled header names the list for anyone who opens
        // it without reading the tooltip. The main menu's Open Recent submenu is already labelled, so it has none.
        var headerItem = new MenuFlyoutItem
        {
            Text = _stringLocalizer.GetString("TitleBar_RecentProjectsHeader"),
            IsEnabled = false
        };
        RecentProjectsFlyout.Items.Add(headerItem);
        RecentProjectsFlyout.Items.Add(new MenuFlyoutSeparator());

        RecentProjectsMenu.Populate(
            RecentProjectsFlyout.Items,
            recentProjects,
            OpenRecentProjectFromSwitcher,
            _stringLocalizer.GetString("MainMenu_ClearRecentProjects"),
            viewModel.ClearRecentProjects);
    }

    private void RecentProjectsButton_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Open the switcher (anchored to the whole button so it aligns to the button's left edge) and mark the
        // tap handled so it does not reach the button's own click; opening it must never also navigate.
        FlyoutBase.ShowAttachedFlyout(WorkspaceButton);
        e.Handled = true;
    }

    private async void OpenRecentProjectFromSwitcher(string projectFilePath)
    {
        if (_recentProjectsViewModel is null)
        {
            return;
        }

        // async void: observe exceptions so a failed open (e.g. the project moved or was deleted) cannot crash
        // on the UI thread.
        try
        {
            await _recentProjectsViewModel.OpenRecentProjectAsync(projectFilePath);
        }
        catch (Exception ex)
        {
            var logger = ServiceLocator.AcquireService<Logging.ILogger<PageNavigationToolbar>>();
            logger.LogError(ex, "Failed to open recent project from the switcher");
        }
    }

    private void ApplyTooltips()
    {
        var mainMenuTooltip = _stringLocalizer.GetString("TitleBar_MainMenuTooltip");
        ToolTipService.SetToolTip(MainMenuButton, mainMenuTooltip);
        ToolTipService.SetPlacement(MainMenuButton, PlacementMode.Bottom);
        AutomationProperties.SetName(MainMenuButton, mainMenuTooltip);

        // The switcher chevron carries only an icon, so give it a tooltip and an accessible name.
        var recentProjectsTooltip = _stringLocalizer.GetString("MainMenu_OpenRecent");
        ToolTipService.SetToolTip(RecentProjectsButton, recentProjectsTooltip);
        ToolTipService.SetPlacement(RecentProjectsButton, PlacementMode.Bottom);
        AutomationProperties.SetName(RecentProjectsButton, recentProjectsTooltip);

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

        ToolTipService.SetToolTip(WorkspaceButton, tooltip);
        ToolTipService.SetPlacement(WorkspaceButton, PlacementMode.Bottom);
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        UpdateNavigationSelection(ApplicationPage.Workspace);
        UpdateWorkspaceTooltip();
    }

    private void OnActivePageChanged(object recipient, ActivePageChangedMessage message)
    {
        UpdateNavigationSelection(message.ActivePage);
    }

    private void UpdateNavigationSelection(ApplicationPage activePage)
    {
        PageNavigation.SelectionChanged -= PageNavigation_SelectionChanged;

        // The Workspace button is custom, so drive its active underline directly from the active page.
        WorkspaceActiveIndicator.Visibility = activePage == ApplicationPage.Workspace
            ? Visibility.Visible
            : Visibility.Collapsed;

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
                    // The Workspace button is custom, not a nav item, so no menu item is selected here.
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

    private void WorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateToPage("Workspace");

        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            return;
        }

        // Focus the active utility so the focus indicator returns to it rather than being dropped on the button.
        // The command runs after this click, so the button does not take focus back.
        var activeUtilityId = workspaceWrapper.WorkspaceService.UtilityPanel.ActiveUtilityId;
        var commandService = ServiceLocator.AcquireService<ICommandService>();
        commandService.Execute<IShowUtilityCommand>(command => command.UtilityId = activeUtilityId);
    }
}
