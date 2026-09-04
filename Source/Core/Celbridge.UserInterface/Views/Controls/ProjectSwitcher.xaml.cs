using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views;

public sealed partial class ProjectSwitcher : UserControl
{
    // The switcher is the fast path to a recent project, so it shows only the most recent few. The complete
    // list is in the File menu's Open Recent submenu. Capping the rows fixes the menu's height, so the
    // commands below them stay on screen however many projects the history holds.
    private const int MaxRecentProjectsShown = 5;

    private readonly IStringLocalizer _stringLocalizer;

    // The recent project rows built on the last open, kept so they can be removed before the next one.
    // Everything else in the menu is declared in XAML and stays in place.
    private readonly List<MenuFlyoutItemBase> _recentProjectItems = new();

    private readonly IProjectHealthService _projectHealthService;

    private ApplicationMenuViewModel? _applicationMenuViewModel;

    public ProjectSwitcherViewModel ViewModel { get; }

    private string NewProjectString => _stringLocalizer.GetString("MainMenu_NewProject");
    private string OpenProjectString => _stringLocalizer.GetString("MainMenu_OpenProject");
    private string ReloadProjectString => _stringLocalizer.GetString("MainMenu_ReloadProject");
    private string CloseProjectString => _stringLocalizer.GetString("MainMenu_CloseProject");
    private string ProjectLoadReportString => _stringLocalizer.GetString("TitleBar_ProjectLoadReport");
    private string CurrentProjectHeaderString => _stringLocalizer.GetString("TitleBar_CurrentProjectHeader");
    private string RecentProjectsHeaderString => _stringLocalizer.GetString("TitleBar_RecentProjectsHeader");

    public ProjectSwitcher()
    {
        // The menu's labels are bound one-time, so the localizer has to be in place before the XAML loads.
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _projectHealthService = ServiceLocator.AcquireService<IProjectHealthService>();
        ViewModel = ServiceLocator.AcquireService<ProjectSwitcherViewModel>();

        this.InitializeComponent();

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
    }

    private void OnProjectSwitcher_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();

        ProjectMenuFlyout.Opening -= OnProjectMenuFlyoutOpening;

        Loaded -= OnProjectSwitcher_Loaded;
        Unloaded -= OnProjectSwitcher_Unloaded;
    }

    private void OnProjectMenuFlyoutOpening(object? sender, object e)
    {
        foreach (var recentProjectItem in _recentProjectItems)
        {
            ProjectMenuFlyout.Items.Remove(recentProjectItem);
        }
        _recentProjectItems.Clear();

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
            ProjectLoadReportSeparator.Visibility = Visibility.Collapsed;
            ProjectLoadReportItem.Visibility = Visibility.Collapsed;
        }
        else
        {
            CurrentProjectItem.Text = currentProject.ProjectName;
            CurrentProjectItem.SecondaryText = DisplayPathFormatter.AbbreviateHomeFolder(currentProject.ProjectFolderPath);
            ToolTipService.SetToolTip(CurrentProjectItem, currentProject.ProjectFilePath);

            CurrentProjectHeader.Visibility = Visibility.Visible;
            CurrentProjectItem.Visibility = Visibility.Visible;
            CurrentProjectSeparator.Visibility = Visibility.Visible;

            UpdateProjectLoadReportItem();
        }

        var recentProjects = viewModel.GetRecentProjects();
        if (recentProjects.Count == 0)
        {
            var emptyItem = new MenuFlyoutItem
            {
                Text = _stringLocalizer.GetString("Menu_NoRecentProjects"),
                IsEnabled = false
            };
            _recentProjectItems.Add(emptyItem);
        }
        else
        {
            var shownProjects = recentProjects.Take(MaxRecentProjectsShown).ToList();

            RecentProjectsMenu.Populate(
                _recentProjectItems,
                shownProjects,
                OpenRecentProjectFromSwitcher,
                _stringLocalizer.GetString("MainMenu_ClearRecentProjects"),
                viewModel.ClearRecentProjects);
        }

        // The rows belong to the recent projects header, so they go in below it rather than at the end of the
        // menu, where the project commands sit.
        var insertIndex = ProjectMenuFlyout.Items.IndexOf(RecentProjectsHeader) + 1;
        foreach (var recentProjectItem in _recentProjectItems)
        {
            ProjectMenuFlyout.Items.Insert(insertIndex, recentProjectItem);
            insertIndex++;
        }
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
        if (!workspaceWrapper.IsWorkspaceLoaded)
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

    private void UpdateProjectLoadReportItem()
    {
        var health = _projectHealthService.CurrentHealth;
        if (health is null)
        {
            // Nothing was recorded for this load, so there is no report to open. The separator goes with the
            // item, which is last in the menu and would otherwise leave it dangling.
            ProjectLoadReportSeparator.Visibility = Visibility.Collapsed;
            ProjectLoadReportItem.Visibility = Visibility.Collapsed;
            return;
        }

        ProjectLoadReportSeparator.Visibility = Visibility.Visible;
        ProjectLoadReportItem.Visibility = Visibility.Visible;
    }

    private void OpenProjectLoadReport(object sender, RoutedEventArgs e)
    {
        var reportResource = _projectHealthService.CurrentHealth?.Resource ?? ResourceKey.Empty;
        if (reportResource.IsEmpty)
        {
            return;
        }

        var commandService = ServiceLocator.AcquireService<ICommandService>();
        commandService.Execute<IOpenDocumentCommand>(command => command.FileResource = reportResource);
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
        ToolTipService.SetToolTip(WorkspaceButton, _stringLocalizer.GetString("TitleBar_SwitchProjectTooltip"));
        ToolTipService.SetPlacement(WorkspaceButton, PlacementMode.Bottom);
    }

    private void WorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        // Anchored to the whole button rather than to a chevron target, so it aligns to the button's left edge.
        FlyoutBase.ShowAttachedFlyout(WorkspaceButton);
    }
}
