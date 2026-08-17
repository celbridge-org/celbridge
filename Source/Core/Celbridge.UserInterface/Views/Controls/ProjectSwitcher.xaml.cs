using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Navigation;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views;

public sealed partial class ProjectSwitcher : UserControl
{
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;

    // The items declared in XAML: the project actions, the current-project section and the recent-projects
    // header. They are kept across rebuilds, and every item added after them is rebuilt on each open.
    private readonly int _staticFlyoutItemCount;

    private readonly IProjectHealthService _projectHealthService;

    private ApplicationMenuViewModel? _applicationMenuViewModel;

    public ProjectSwitcherViewModel ViewModel { get; }

    private string NewProjectString => _stringLocalizer.GetString("MainMenu_NewProject");
    private string OpenProjectString => _stringLocalizer.GetString("MainMenu_OpenProject");
    private string ReloadProjectString => _stringLocalizer.GetString("MainMenu_ReloadProject");
    private string CloseProjectString => _stringLocalizer.GetString("MainMenu_CloseProject");
    private string CurrentProjectHeaderString => _stringLocalizer.GetString("TitleBar_CurrentProjectHeader");
    private string RecentProjectsHeaderString => _stringLocalizer.GetString("TitleBar_RecentProjectsHeader");

    public ProjectSwitcher()
    {
        // The menu's labels are bound one-time, so the localizer has to be in place before the XAML loads.
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _projectHealthService = ServiceLocator.AcquireService<IProjectHealthService>();
        ViewModel = ServiceLocator.AcquireService<ProjectSwitcherViewModel>();

        this.InitializeComponent();

        _staticFlyoutItemCount = ProjectMenuFlyout.Items.Count;

        this.DataContext = ViewModel;

        // The menu drops down over the document area, where a hosted web view would take the click too.
        var overlayInputSuppressor = ServiceLocator.AcquireService<IOverlayInputSuppressor>();
        overlayInputSuppressor.SuppressWhileOpen(ProjectMenuFlyout);

        Loaded += OnProjectSwitcher_Loaded;
        Unloaded += OnProjectSwitcher_Unloaded;
    }

    private void OnProjectSwitcher_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnLoaded();

        _applicationMenuViewModel = ServiceLocator.AcquireService<ApplicationMenuViewModel>();
        ProjectMenuFlyout.Opening += OnProjectMenuFlyoutOpening;

        ApplyTooltips();

        _messengerService.Register<ActivePageChangedMessage>(this, OnActivePageChanged);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
    }

    private void OnProjectSwitcher_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();

        ProjectMenuFlyout.Opening -= OnProjectMenuFlyoutOpening;

        Loaded -= OnProjectSwitcher_Loaded;
        Unloaded -= OnProjectSwitcher_Unloaded;

        _messengerService.UnregisterAll(this);
    }

    private void OnProjectMenuFlyoutOpening(object? sender, object e)
    {
        while (ProjectMenuFlyout.Items.Count > _staticFlyoutItemCount)
        {
            ProjectMenuFlyout.Items.RemoveAt(_staticFlyoutItemCount);
        }

        var viewModel = _applicationMenuViewModel;
        if (viewModel is null)
        {
            return;
        }

        // GetRecentProjects excludes the open project, so it is never listed twice.
        var currentProject = viewModel.GetCurrentProject();
        if (currentProject is null)
        {
            CurrentProjectHeader.Visibility = Visibility.Collapsed;
            CurrentProjectItem.Visibility = Visibility.Collapsed;
            CurrentProjectSeparator.Visibility = Visibility.Collapsed;
            ProjectHealthItem.Visibility = Visibility.Collapsed;
        }
        else
        {
            CurrentProjectItem.Text = currentProject.ProjectName;
            CurrentProjectItem.SecondaryText = DisplayPathFormatter.AbbreviateHomeFolder(currentProject.ProjectFolderPath);
            ToolTipService.SetToolTip(CurrentProjectItem, currentProject.ProjectFilePath);

            CurrentProjectHeader.Visibility = Visibility.Visible;
            CurrentProjectItem.Visibility = Visibility.Visible;
            CurrentProjectSeparator.Visibility = Visibility.Visible;

            UpdateProjectHealthItem();
        }

        var recentProjects = viewModel.GetRecentProjects();
        if (recentProjects.Count == 0)
        {
            var emptyItem = new MenuFlyoutItem
            {
                Text = _stringLocalizer.GetString("Menu_NoRecentProjects"),
                IsEnabled = false
            };
            ProjectMenuFlyout.Items.Add(emptyItem);
            return;
        }

        RecentProjectsMenu.Populate(
            ProjectMenuFlyout.Items,
            recentProjects,
            OpenRecentProjectFromSwitcher,
            _stringLocalizer.GetString("MainMenu_ClearRecentProjects"),
            viewModel.ClearRecentProjects);
    }

    private void NewProject(object sender, RoutedEventArgs e)
    {
        _applicationMenuViewModel?.NewProject();
    }

    private void OpenProject(object sender, RoutedEventArgs e)
    {
        _applicationMenuViewModel?.OpenProject();
    }

    private void ReloadProject(object sender, RoutedEventArgs e)
    {
        _applicationMenuViewModel?.ReloadProject();
    }

    private void CloseProject(object sender, RoutedEventArgs e)
    {
        if (_applicationMenuViewModel is null)
        {
            return;
        }

        _ = _applicationMenuViewModel.CloseProjectAsync();
    }

    private void ReturnToCurrentProject(object sender, RoutedEventArgs e)
    {
        // The row names the open project rather than commanding anything, so clicking it just dismisses the
        // menu and hands focus back to the document the user came from.
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            return;
        }

        var activeDocument = workspaceWrapper.WorkspaceService.DocumentsService.ActiveDocument;
        if (activeDocument.IsEmpty)
        {
            return;
        }

        var commandService = ServiceLocator.AcquireService<ICommandService>();
        commandService.Execute<IActivateDocumentCommand>(command => command.FileResource = activeDocument);
    }

    private void UpdateProjectHealthItem()
    {
        var health = _projectHealthService.CurrentHealth;
        if (health is null)
        {
            // Nothing was recorded for this load, so there is no report to open.
            ProjectHealthItem.Visibility = Visibility.Collapsed;
            return;
        }

        ProjectHealthItem.Text = _stringLocalizer.GetString("TitleBar_ViewLoadReport");
        ProjectHealthIcon.Symbol = IconSymbol.Report;

        ProjectHealthItem.Visibility = Visibility.Visible;
    }

    private void OpenProjectHealthReport(object sender, RoutedEventArgs e)
    {
        var reportResource = _projectHealthService.CurrentHealth?.Resource ?? ResourceKey.Empty;
        if (reportResource.IsEmpty)
        {
            return;
        }

        var commandService = ServiceLocator.AcquireService<ICommandService>();
        commandService.Execute<IOpenDocumentCommand>(command => command.FileResource = reportResource);
    }

    private void ProjectMenuButton_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Open the menu (anchored to the whole button so it aligns to the button's left edge) and mark the
        // tap handled so it does not reach the button's own click. Opening it must never also navigate.
        FlyoutBase.ShowAttachedFlyout(WorkspaceButton);
        e.Handled = true;
    }

    private async void OpenRecentProjectFromSwitcher(string projectFilePath)
    {
        if (_applicationMenuViewModel is null)
        {
            return;
        }

        // async void: observe exceptions so a failed open (e.g. the recent project moved or was deleted) cannot
        // crash on the UI thread.
        try
        {
            await _applicationMenuViewModel.OpenRecentProjectAsync(projectFilePath);
        }
        catch (Exception ex)
        {
            var logger = ServiceLocator.AcquireService<Logging.ILogger<ProjectSwitcher>>();
            logger.LogError(ex, "Failed to open recent project from the switcher");
        }
    }

    private void ApplyTooltips()
    {
        // The menu chevron carries only an icon, so give it a tooltip and an accessible name.
        var projectMenuTooltip = _stringLocalizer.GetString("TitleBar_ProjectMenuTooltip");
        ToolTipService.SetToolTip(ProjectMenuButton, projectMenuTooltip);
        ToolTipService.SetPlacement(ProjectMenuButton, PlacementMode.Bottom);
        AutomationProperties.SetName(ProjectMenuButton, projectMenuTooltip);

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
