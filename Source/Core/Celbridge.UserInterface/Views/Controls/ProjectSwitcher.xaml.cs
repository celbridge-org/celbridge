using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Navigation;
using Celbridge.Platform;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views;

public sealed partial class ProjectSwitcher : UserControl
{
    // The current-project row and its separator are declared in XAML and kept across rebuilds; every item
    // after them is a recent project and is rebuilt each time the flyout opens.
    private const int StaticFlyoutItemCount = 2;

    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private MainMenuViewModel? _recentProjectsViewModel;

    public ProjectSwitcherViewModel ViewModel { get; }

    public ProjectSwitcher()
    {
        this.InitializeComponent();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<ProjectSwitcherViewModel>();

        this.DataContext = ViewModel;

        Loaded += OnProjectSwitcher_Loaded;
        Unloaded += OnProjectSwitcher_Unloaded;
    }

    private void OnProjectSwitcher_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnLoaded();

        // The switcher reuses the main menu view model for the recent-projects list and open logic.
        _recentProjectsViewModel = ServiceLocator.AcquireService<MainMenuViewModel>();
        RecentProjectsFlyout.Opening += OnRecentProjectsFlyoutOpening;

        ApplyTooltips();

        _messengerService.Register<ActivePageChangedMessage>(this, OnActivePageChanged);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
    }

    private void OnProjectSwitcher_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();

        RecentProjectsFlyout.Opening -= OnRecentProjectsFlyoutOpening;

        Loaded -= OnProjectSwitcher_Loaded;
        Unloaded -= OnProjectSwitcher_Unloaded;

        _messengerService.UnregisterAll(this);
    }

    private void OnRecentProjectsFlyoutOpening(object? sender, object e)
    {
        while (RecentProjectsFlyout.Items.Count > StaticFlyoutItemCount)
        {
            RecentProjectsFlyout.Items.RemoveAt(StaticFlyoutItemCount);
        }

        var viewModel = _recentProjectsViewModel;
        if (viewModel is null)
        {
            return;
        }

        // GetRecentProjects excludes the open project, so it is never listed twice.
        var currentProject = viewModel.GetCurrentProject();
        if (currentProject is null)
        {
            CurrentProjectItem.Visibility = Visibility.Collapsed;
            CurrentProjectSeparator.Visibility = Visibility.Collapsed;
        }
        else
        {
            CurrentProjectItem.Text = currentProject.ProjectName;
            CurrentProjectItem.KeyboardAcceleratorTextOverride = currentProject.ProjectFilePath;
            CurrentProjectItem.Visibility = Visibility.Visible;
            CurrentProjectSeparator.Visibility = Visibility.Visible;
        }

        var recentProjects = viewModel.GetRecentProjects();
        if (recentProjects.Count == 0)
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

        RecentProjectsMenu.Populate(
            RecentProjectsFlyout.Items,
            recentProjects,
            OpenRecentProjectFromSwitcher,
            _stringLocalizer.GetString("MainMenu_ClearRecentProjects"),
            viewModel.ClearRecentProjects);
    }

    private void ReloadCurrentProject(object sender, RoutedEventArgs e)
    {
        // The button handles the pointer, so the row's own click never runs and the flyout stays open.
        RecentProjectsFlyout.Hide();
        _recentProjectsViewModel?.ReloadProject();
    }

    private void ShowCurrentProject(object sender, RoutedEventArgs e)
    {
        RecentProjectsFlyout.Hide();
        _recentProjectsViewModel?.ShowProject();
    }

    private void RecentProjectsButton_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Open the switcher (anchored to the whole button so it aligns to the button's left edge) and mark the
        // tap handled so it does not reach the button's own click. Opening it must never also navigate.
        FlyoutBase.ShowAttachedFlyout(WorkspaceButton);
        e.Handled = true;
    }

    private async void OpenRecentProjectFromSwitcher(string projectFilePath)
    {
        if (_recentProjectsViewModel is null)
        {
            return;
        }

        // async void: observe exceptions so a failed open (e.g. the recent project moved or was deleted) cannot
        // crash on the UI thread.
        try
        {
            await _recentProjectsViewModel.OpenRecentProjectAsync(projectFilePath);
        }
        catch (Exception ex)
        {
            var logger = ServiceLocator.AcquireService<Logging.ILogger<ProjectSwitcher>>();
            logger.LogError(ex, "Failed to open recent project from the switcher");
        }
    }

    private void ApplyTooltips()
    {
        // The switcher chevron carries only an icon, so give it a tooltip and an accessible name.
        var recentProjectsTooltip = _stringLocalizer.GetString("MainMenu_OpenRecent");
        ToolTipService.SetToolTip(RecentProjectsButton, recentProjectsTooltip);
        ToolTipService.SetPlacement(RecentProjectsButton, PlacementMode.Bottom);
        AutomationProperties.SetName(RecentProjectsButton, recentProjectsTooltip);

        var reloadProjectTooltip = _stringLocalizer.GetString("MainMenu_ReloadProject");
        ToolTipService.SetToolTip(ReloadProjectButton, reloadProjectTooltip);
        AutomationProperties.SetName(ReloadProjectButton, reloadProjectTooltip);

        var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        var fileManagerName = _stringLocalizer.GetString(platformInfo.FileManagerNameStringKey);
        var showProjectTooltip = _stringLocalizer.GetString("TitleBar_ShowProjectInFileManager", fileManagerName);
        ToolTipService.SetToolTip(ShowProjectButton, showProjectTooltip);
        AutomationProperties.SetName(ShowProjectButton, showProjectTooltip);

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
        UpdateActiveIndicator(ApplicationPage.Workspace);
        UpdateWorkspaceTooltip();
    }

    private void OnActivePageChanged(object recipient, ActivePageChangedMessage message)
    {
        UpdateActiveIndicator(message.ActivePage);
    }

    private void UpdateActiveIndicator(ApplicationPage activePage)
    {
        // The switcher is a custom button rather than a nav item, so its active underline is driven directly
        // from the active page.
        WorkspaceActiveIndicator.Visibility = activePage == ApplicationPage.Workspace
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void WorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateToWorkspace();

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
