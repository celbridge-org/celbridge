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
    private readonly IFocusService _focusService;
    private bool _isShowingTab;

    public IExplorerPanel ExplorerPanel { get; }
    public ISearchPanel SearchPanel { get; }

    public ActivityPanelTab CurrentTab => ViewModel.CurrentTab;

    public ActivityPanelViewModel ViewModel { get; }

    public ActivityPanel()
    {
        this.InitializeComponent();

        _logger = ServiceLocator.AcquireService<ILogger<ActivityPanel>>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _dispatcher = ServiceLocator.AcquireService<IDispatcher>();
        _focusService = ServiceLocator.AcquireService<IFocusService>();

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
        Unloaded += ActivityPanel_Unloaded;
    }

    private void ActivityPanel_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTooltips();

        // Register how the hosted panels take keyboard focus, so the focus service can return focus to
        // whichever is focused after a modal dialog closes or the resource tree rebuilds. Only Explorer
        // and Search register a handler by design: the Documents and Console web surfaces and the
        // Inspector intentionally have none, so focus restore is a deliberate no-op for those and the
        // user re-focuses them with a single click.
        _focusService.SetPanelFocusHandler(WorkspacePanel.Explorer, ExplorerPanel.FocusPanel);
        _focusService.SetPanelFocusHandler(WorkspacePanel.Search, SearchPanel.FocusSearchInput);

        // Set Explorer as the initially selected nav item
        ActivityNavigation.SelectedItem = ExplorerNavItem;
    }

    private void ActivityPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        _focusService.SetPanelFocusHandler(WorkspacePanel.Explorer, null);
        _focusService.SetPanelFocusHandler(WorkspacePanel.Search, null);
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

    public void ShowTab(ActivityPanelTab tab)
    {
        if (tab == ActivityPanelTab.None)
        {
            return;
        }

        // Setting ActivityNavigation.SelectedItem below raises SelectionChanged, which calls back
        // into ShowTab; this guard makes that re-entrant call a no-op.
        if (_isShowingTab)
        {
            return;
        }

        _isShowingTab = true;
        try
        {
            // Hide all panels
            ExplorerPanelControl.Visibility = Visibility.Collapsed;
            SearchPanelControl.Visibility = Visibility.Collapsed;

            // Show the requested panel and highlight its activity-bar item, then move keyboard focus into
            // the panel. Focusing the panel content reports the panel through the central tracker, so no
            // explicit focus claim is made here. The focus is deferred so it lands after the NavigationView
            // finishes moving focus onto the selected rail item (which would otherwise clear it again).
            switch (tab)
            {
                case ActivityPanelTab.Explorer:
                    ExplorerPanelControl.Visibility = Visibility.Visible;
                    ActivityNavigation.SelectedItem = ExplorerNavItem;
                    _dispatcher.TryEnqueue(() => ExplorerPanel.FocusPanel());
                    break;
                case ActivityPanelTab.Search:
                    SearchPanelControl.Visibility = Visibility.Visible;
                    ActivityNavigation.SelectedItem = SearchNavItem;
                    _dispatcher.TryEnqueue(() => SearchPanel.FocusSearchInput());
                    break;
                default:
                    _logger.LogWarning($"Tab not yet implemented: {tab}");
                    return;
            }

            ViewModel.CurrentTab = tab;
        }
        finally
        {
            _isShowingTab = false;
        }
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
