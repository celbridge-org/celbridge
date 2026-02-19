using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Search;
using Celbridge.WorkspaceUI.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.WorkspaceUI.Views;

public sealed partial class ActivityPanel : UserControl, IActivityPanel
{
    private readonly ILogger<ActivityPanel> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDispatcher _dispatcher;
    private readonly IPanelFocusService _panelFocusService;

    public IExplorerPanel ExplorerPanel { get; }
    public ISearchPanel SearchPanel { get; }

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
        _dispatcher = ServiceLocator.AcquireService<IDispatcher>();
        _panelFocusService = ServiceLocator.AcquireService<IPanelFocusService>();

        // Acquire panel views via DI and host them in ContentControls
        ExplorerPanel = ServiceLocator.AcquireService<IExplorerPanel>();
        SearchPanel = ServiceLocator.AcquireService<ISearchPanel>();
        ExplorerPanelControl.Content = ExplorerPanel as UIElement;
        SearchPanelControl.Content = SearchPanel as UIElement;

        ViewModel = ServiceLocator.AcquireService<ActivityPanelViewModel>();
        DataContext = ViewModel;

        // Show the Explorer tab by default
        ShowTab(ActivityPanelTab.Explorer);

        Loaded += ActivityPanel_Loaded;
    }

    private void ActivityPanel_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTooltips();

        // Set Explorer as the initially selected nav item
        ActivityNavigation.SelectedItem = ExplorerNavItem;
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
