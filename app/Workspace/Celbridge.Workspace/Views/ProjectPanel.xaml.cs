using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Workspace.Services;
using Celbridge.Workspace.ViewModels;
using Microsoft.Extensions.Localization;
using System.Text;

namespace Celbridge.Workspace.Views;

public sealed partial class ProjectPanel : UserControl
{
    private readonly ILogger<ProjectPanel> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IProjectService _projectService;

    private ShortcutMenuBuilder? _shortcutMenuBuilder;
    private readonly Dictionary<ProjectPanelTab, UIElement> _panels = new();

    /// <summary>
    /// Gets the currently active panel tab.
    /// </summary>
    public ProjectPanelTab CurrentTab => ViewModel.CurrentTab;

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

    private void ProjectPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_shortcutMenuBuilder != null)
        {
            _shortcutMenuBuilder.ShortcutClicked -= OnShortcutClicked;
        }

        Loaded -= ProjectPanel_Loaded;
        Unloaded -= ProjectPanel_Unloaded;
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
    /// Registers a panel with the panel manager.
    /// </summary>
    public void RegisterPanel(ProjectPanelTab tab, UIElement panel)
    {
        if (tab == ProjectPanelTab.None)
        {
            _logger.LogWarning("Cannot register panel with ProjectPanelTab.None");
            return;
        }

        if (_panels.ContainsKey(tab))
        {
            _logger.LogWarning($"Panel for {tab} is already registered. Replacing with new panel.");
            ContentArea.Children.Remove(_panels[tab]);
        }

        _panels[tab] = panel;
        ContentArea.Children.Add(panel);
        panel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Unregisters a panel from the panel manager.
    /// </summary>
    public void UnregisterPanel(ProjectPanelTab tab)
    {
        if (_panels.TryGetValue(tab, out var panel))
        {
            ContentArea.Children.Remove(panel);
            _panels.Remove(tab);

            // If we removed the currently visible panel, show another one
            if (ViewModel.CurrentTab == tab)
            {
                var nextTab = _panels.Keys.FirstOrDefault(t => t != ProjectPanelTab.None);
                if (nextTab != ProjectPanelTab.None)
                {
                    ShowTab(nextTab);
                }
                else
                {
                    ViewModel.CurrentTab = ProjectPanelTab.None;
                }
            }
        }
    }

    /// <summary>
    /// Shows the specified panel tab and hides all others.
    /// </summary>
    public void ShowTab(ProjectPanelTab tab)
    {
        if (tab == ProjectPanelTab.None)
        {
            return;
        }

        if (!_panels.TryGetValue(tab, out var panelToShow))
        {
            _logger.LogWarning($"No panel registered for tab: {tab}");
            return;
        }

        // Hide all panels
        foreach (var panel in _panels.Values)
        {
            panel.Visibility = Visibility.Collapsed;
        }

        // Show the requested panel
        panelToShow.Visibility = Visibility.Visible;
        ViewModel.CurrentTab = tab;

        // Set focus to the search input when switching to the Search tab
        if (tab == ProjectPanelTab.Search &&
            panelToShow is ISearchPanel searchPanel)
        {
            // Use dispatcher to ensure the panel is fully loaded before focusing
            _ = panelToShow.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                searchPanel.FocusSearchInput();
            });
        }
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

            // Handle built-in navigation items
            if (Enum.TryParse<ProjectPanelTab>(tag, out var tab) &&
                tab != ProjectPanelTab.None)
            {
                ShowTab(tab);
            }
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
