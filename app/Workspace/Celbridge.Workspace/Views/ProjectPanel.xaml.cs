using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Workspace.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Workspace.Views;

public sealed partial class ProjectPanel : UserControl
{
    private readonly ILogger<ProjectPanel> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IProjectService _projectService;

    private Dictionary<string, string> _tagsToScriptDictionary = new();
    private List<KeyValuePair<IList<object>, NavigationViewItem>> _shortcutMenuItems = new();
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

        // Register for shortcut menu rebuilds
        _projectService.RegisterRebuildShortcutsUI(BuildShortcutMenuItems);
    }

    private void ProjectPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        _projectService.UnregisterRebuildShortcutsUI(BuildShortcutMenuItems);

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
            if (_tagsToScriptDictionary.TryGetValue(tag, out var script))
            {
                if (!string.IsNullOrEmpty(script))
                {
                    var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
                    workspaceWrapper.WorkspaceService.ConsoleService.RunCommand(script);
                }
                return;
            }

            // Handle built-in navigation items
            switch (tag)
            {
                case "Explorer":
                    ShowPanel(_explorerPanel, _searchPanel);
                    NotifyProjectPanelViewChange(ProjectPanelView.Explorer);
                    break;

                case "Search":
                    ShowPanel(_searchPanel, _explorerPanel);
                    NotifyProjectPanelViewChange(ProjectPanelView.Search);
                    break;

                case "Debug":
                    // Placeholder - Debug panel not yet implemented
                    NotifyProjectPanelViewChange(ProjectPanelView.Debug);
                    break;

                case "SourceControl":
                    // Placeholder - Source Control panel not yet implemented
                    NotifyProjectPanelViewChange(ProjectPanelView.VersionControl);
                    break;
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

    private void BuildShortcutMenuItems(object sender, IProjectService.RebuildShortcutsUIEventArgs args)
    {
        _tagsToScriptDictionary.Clear();
        foreach (var (menuItems, menuItem) in _shortcutMenuItems)
        {
            menuItems.Remove(menuItem);
        }
        _shortcutMenuItems.Clear();

        NavigationBarSection.CustomCommandNode node = args.NavigationBarSection.RootCustomCommandNode;
        AddShortcutMenuItems(node, ProjectNavigation.MenuItems);
    }

    private void AddShortcutMenuItems(NavigationBarSection.CustomCommandNode node, IList<object> menuItems)
    {
        Dictionary<string, NavigationViewItem> newNodes = new();
        Dictionary<string, string> pathToScriptDictionary = new();

        foreach (var (k, v) in node.Nodes)
        {
            var newItem = new NavigationViewItem()
            {
                Name = k,
                Content = k
            };

            menuItems.Add(newItem);
            string newPath = v.Path + (v.Path.Length > 0 ? "." : "") + k;
            newNodes.Add(newPath, newItem);
            _shortcutMenuItems.Add(new KeyValuePair<IList<object>, NavigationViewItem>(menuItems, newItem));
            AddShortcutMenuItems(v, newItem.MenuItems);
        }

        foreach (var command in node.CustomCommands)
        {
            if (pathToScriptDictionary.ContainsKey(command.Path!))
            {
                _logger.LogWarning($"Shortcut command '{command.Name}' at path '{command.Path}' collides with an existing command; command will not be added.");
                continue;
            }

            Symbol? icon = null;
            if (command.Icon is not null)
            {
                if (Enum.TryParse(command.Icon, out Symbol parsedIcon))
                {
                    icon = parsedIcon;
                }
            }

            if (newNodes.ContainsKey(command.Path!))
            {
                NavigationViewItem item = newNodes[command.Path!];
                if (!string.IsNullOrEmpty(command.ToolTip))
                {
                    ToolTipService.SetToolTip(item, command.ToolTip);
                    ToolTipService.SetPlacement(item, PlacementMode.Right);
                }

                if (icon.HasValue)
                {
                    item.Icon = new SymbolIcon(icon.Value);
                }

                if (!string.IsNullOrEmpty(command.Name))
                {
                    item.Content = command.Name;
                }

                _logger.LogWarning($"Shortcut command '{command.Name}' at path '{command.Path}' collides with a folder node.");
                continue;
            }

            _tagsToScriptDictionary.Add(command.Path!, command.Script!);

            var commandItem = new NavigationViewItem
            {
                Name = command.Name ?? "Shortcut",
                Content = command.Name ?? "Shortcut",
                Tag = command.Path!
            };

            if (!string.IsNullOrEmpty(command.ToolTip))
            {
                ToolTipService.SetToolTip(commandItem, command.ToolTip);
                ToolTipService.SetPlacement(commandItem, PlacementMode.Right);
            }

            if (icon.HasValue)
            {
                commandItem.Icon = new SymbolIcon(icon.Value);
            }

            menuItems.Add(commandItem);
            _shortcutMenuItems.Add(new KeyValuePair<IList<object>, NavigationViewItem>(menuItems, commandItem));
        }
    }
}
