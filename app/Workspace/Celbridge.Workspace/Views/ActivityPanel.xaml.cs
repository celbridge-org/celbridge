using System.Text;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Search;
using Celbridge.UserInterface;
using Celbridge.Workspace.Services;
using Celbridge.Workspace.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Workspace.Views;

public sealed partial class ActivityPanel : UserControl, IActivityPanel
{
    private readonly ILogger<ActivityPanel> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IProjectService _projectService;
    private readonly IDispatcher _dispatcher;
    private readonly IPanelFocusService _panelFocusService;

    private ShortcutMenuBuilder? _shortcutMenuBuilder;

    public IExplorerPanel ExplorerPanel => ExplorerPanelControl;
    public ISearchPanel SearchPanel => SearchPanelControl;

    /// <summary>
    /// Gets the currently active panel tab.
    /// </summary>
    public ActivityPanelTab CurrentTab => ViewModel.CurrentTab;

    public ActivityPanelViewModel ViewModel { get; }

    public ActivityPanel()
    {
        this.InitializeComponent();

        _logger = ServiceLocator.AcquireService<ILogger<ActivityPanel>>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _projectService = ServiceLocator.AcquireService<IProjectService>();
        _dispatcher = ServiceLocator.AcquireService<IDispatcher>();
        _panelFocusService = ServiceLocator.AcquireService<IPanelFocusService>();

        ViewModel = ServiceLocator.AcquireService<ActivityPanelViewModel>();
        DataContext = ViewModel;

        // Show the Explorer tab by default
        ShowTab(ActivityPanelTab.Explorer);

        Loaded += ActivityPanel_Loaded;
        Unloaded += ActivityPanel_Unloaded;
    }

    private void ActivityPanel_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTooltips();

        // Set Explorer as the initially selected nav item
        ActivityNavigation.SelectedItem = ExplorerNavItem;

        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            return;
        }

        var shortcutsSection = currentProject.ProjectConfig.Config.Shortcuts;
        var logger = ServiceLocator.AcquireService<ILogger<ShortcutMenuBuilder>>();
        _shortcutMenuBuilder = new ShortcutMenuBuilder(logger);
        _shortcutMenuBuilder.ShortcutClicked += OnShortcutClicked;

        // Don't build shortcuts when there are validation errors
        // Error notification is handled by WorkspaceLoader
        if (shortcutsSection.HasErrors)
        {
            ViewModel.HasShortcuts = false;
            ShortcutButtonsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var hasShortcuts = _shortcutMenuBuilder.BuildShortcutButtons(shortcutsSection, ShortcutButtonsPanel);
        ViewModel.HasShortcuts = hasShortcuts;
        ShortcutButtonsPanel.Visibility = hasShortcuts ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ActivityPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_shortcutMenuBuilder != null)
        {
            _shortcutMenuBuilder.ShortcutClicked -= OnShortcutClicked;
        }

        Loaded -= ActivityPanel_Loaded;
        Unloaded -= ActivityPanel_Unloaded;
    }

    private void OnShortcutClicked(string tag)
    {
        if (_shortcutMenuBuilder?.TryGetScript(tag, out var script) == true && !string.IsNullOrEmpty(script))
        {
            var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
            workspaceWrapper.WorkspaceService.ConsoleService.RunCommand(script);
        }
    }

    private void ApplyTooltips()
    {
        var explorerTooltip = _stringLocalizer.GetString("ActivityPanel_ExplorerTooltip");
        ToolTipService.SetToolTip(ExplorerNavItem, explorerTooltip);
        ToolTipService.SetPlacement(ExplorerNavItem, PlacementMode.Right);

        var searchTooltip = _stringLocalizer.GetString("ActivityPanel_SearchTooltip");
        ToolTipService.SetToolTip(SearchNavItem, searchTooltip);
        ToolTipService.SetPlacement(SearchNavItem, PlacementMode.Right);

        var debugTooltip = _stringLocalizer.GetString("ActivityPanel_DebugTooltip");
        ToolTipService.SetToolTip(DebugNavItem, debugTooltip);
        ToolTipService.SetPlacement(DebugNavItem, PlacementMode.Right);

        var sourceControlTooltip = _stringLocalizer.GetString("ActivityPanel_SourceControlTooltip");
        ToolTipService.SetToolTip(SourceControlNavItem, sourceControlTooltip);
        ToolTipService.SetPlacement(SourceControlNavItem, PlacementMode.Right);
    }

    /// <summary>
    /// Shows the specified panel tab and hides all others.
    /// </summary>
    public void ShowTab(ActivityPanelTab tab)
    {
        if (tab == ActivityPanelTab.None)
        {
            return;
        }

        // Hide all panels
        ExplorerPanelControl.Visibility = Visibility.Collapsed;
        SearchPanelControl.Visibility = Visibility.Collapsed;

        // Show the requested panel and set focus
        switch (tab)
        {
            case ActivityPanelTab.Explorer:
                ExplorerPanelControl.Visibility = Visibility.Visible;
                _panelFocusService.SetFocusedPanel(WorkspacePanel.Explorer);
                break;
            case ActivityPanelTab.Search:
                SearchPanelControl.Visibility = Visibility.Visible;
                _panelFocusService.SetFocusedPanel(WorkspacePanel.Search);
                // Use dispatcher to ensure the panel is fully loaded before focusing
                _dispatcher.TryEnqueue(() => SearchPanel.FocusSearchInput());
                break;
            default:
                _logger.LogWarning($"Tab not yet implemented: {tab}");
                return;
        }

        ViewModel.CurrentTab = tab;
    }

    private void ActivityNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem selectedItem)
        {
            var tag = selectedItem.Tag?.ToString();
            if (string.IsNullOrEmpty(tag))
            {
                return;
            }

            // Handle built-in navigation items
            if (Enum.TryParse<ActivityPanelTab>(tag, out var tab) &&
                tab != ActivityPanelTab.None)
            {
                ShowTab(tab);
            }
        }
    }
}
