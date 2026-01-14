using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Workspace.Services;
using Celbridge.Workspace.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Workspace.Views;

public sealed partial class ProjectPanel : UserControl
{
    private readonly ILogger<ProjectPanel> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IProjectService _projectService;

    private ShortcutMenuBuilder? _shortcutMenuBuilder;
    private UIElement? _explorerPanel;
    private UIElement? _searchPanel;

    public ProjectPanelViewModel ViewModel { get; }

    public ProjectPanel()
    {
        this.InitializeComponent();

        _logger = ServiceLocator.AcquireService<ILogger<ProjectPanel>>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _projectService = ServiceLocator.AcquireService<IProjectService>();

        ViewModel = ServiceLocator.AcquireService<ProjectPanelViewModel>();
        DataContext = ViewModel;

        Loaded += ProjectPanel_Loaded;
        Unloaded += ProjectPanel_Unloaded;
    }

    private void ProjectPanel_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTooltips();

        // Build shortcut menu items from the current project configuration
        var currentProject = _projectService.CurrentProject;
        if (currentProject != null)
        {
            var navigationBarSection = currentProject.ProjectConfig.Config.Shortcuts.NavigationBar;
            var logger = ServiceLocator.AcquireService<ILogger<ShortcutMenuBuilder>>();
            _shortcutMenuBuilder = new ShortcutMenuBuilder(logger);
            _shortcutMenuBuilder.BuildShortcutMenuItems(navigationBarSection.RootCustomCommandNode, ProjectNavigation.MenuItems);
        }
    }

    private void ProjectPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ProjectPanel_Loaded;
        Unloaded -= ProjectPanel_Unloaded;
    }

    private void ApplyTooltips()
    {
        var explorerTooltip = _stringLocalizer.GetString("ProjectPanel_ExplorerTooltip");
        ToolTipService.SetToolTip(ExplorerNavItem, explorerTooltip);
        ToolTipService.SetPlacement(ExplorerNavItem, PlacementMode.Right);

        var searchTooltip = _stringLocalizer.GetString("ProjectPanel_SearchTooltip");
        ToolTipService.SetToolTip(SearchNavItem, searchTooltip);
        ToolTipService.SetPlacement(SearchNavItem, PlacementMode.Right);

        var debugTooltip = _stringLocalizer.GetString("ProjectPanel_DebugTooltip");
        ToolTipService.SetToolTip(DebugNavItem, debugTooltip);
        ToolTipService.SetPlacement(DebugNavItem, PlacementMode.Right);

        var sourceControlTooltip = _stringLocalizer.GetString("ProjectPanel_SourceControlTooltip");
        ToolTipService.SetToolTip(SourceControlNavItem, sourceControlTooltip);
        ToolTipService.SetPlacement(SourceControlNavItem, PlacementMode.Right);
    }

    /// <summary>
    /// Populates the ProjectPanel with the explorer and search panels from the workspace service.
    /// </summary>
    public void PopulatePanels(UIElement explorerPanel, UIElement searchPanel)
    {
        _explorerPanel = explorerPanel;
        _searchPanel = searchPanel;

        // Add both panels to the content area, initially only explorer is visible
        ContentArea.Children.Add(_explorerPanel);
        ContentArea.Children.Add(_searchPanel);

        _searchPanel.Visibility = Visibility.Collapsed;
        _explorerPanel.Visibility = Visibility.Visible;
    }

    private void ProjectNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem selectedItem)
        {
            var tag = selectedItem.Tag?.ToString();
            if (string.IsNullOrEmpty(tag))
            {
                return;
            }

            // Check if this is a shortcut command
            if (_shortcutMenuBuilder?.TryGetScript(tag, out var script) == true)
            {
                if (!string.IsNullOrEmpty(script))
                {
                    var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
                    workspaceWrapper.WorkspaceService.ConsoleService.RunCommand(script);
                }
                return;
            }

            // Handle built-in navigation items
            if (Enum.TryParse<ProjectPanelView>(tag, out var view) && view != ProjectPanelView.None)
            {
                switch (view)
                {
                    case ProjectPanelView.Explorer:
                        ShowPanel(_explorerPanel, _searchPanel);
                        break;

                    case ProjectPanelView.Search:
                        ShowPanel(_searchPanel, _explorerPanel);
                        break;
                }

                NotifyProjectPanelViewChange(view);
            }
        }
    }

    private void ShowPanel(UIElement? panelToShow, UIElement? panelToHide)
    {
        if (panelToHide != null)
        {
            panelToHide.Visibility = Visibility.Collapsed;
        }
        if (panelToShow != null)
        {
            panelToShow.Visibility = Visibility.Visible;
        }
    }

    private void NotifyProjectPanelViewChange(ProjectPanelView view)
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        if (workspaceWrapper.IsWorkspacePageLoaded)
        {
            workspaceWrapper.WorkspaceService.ProjectPanelService.ShowView(view);
        }
    }

    /// <summary>
    /// Selects a navigation item by its tag.
    /// </summary>
    public void SelectNavigationItem(string tag)
    {
        foreach (var item in ProjectNavigation.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == tag)
            {
                ProjectNavigation.SelectedItem = navItem;
                return;
            }
        }
    }
}
