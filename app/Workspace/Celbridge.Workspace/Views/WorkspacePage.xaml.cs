using Celbridge.Messaging;
using Celbridge.Workspace.Services;
using Celbridge.Workspace.ViewModels;
using Celbridge.Console.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.Workspace.Views;

public sealed partial class WorkspacePage : Celbridge.UserInterface.Views.PersistentPage
{
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;

    public WorkspacePageViewModel ViewModel { get; }

    private bool Initialised = false;

    public WorkspacePage()
    {
        InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<WorkspacePageViewModel>();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        DataContext = ViewModel;

        Loaded += WorkspacePage_Loaded;

        Unloaded += WorkspacePage_Unloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.LoadProjectCancellationToken = e.Parameter as CancellationTokenSource;
    }

    private void WorkspacePage_Loaded(object sender, RoutedEventArgs e)
    {
        // Only execute this functionality if we have Cache Mode set to Disabled.
        //  - This means we are purposefully wanted to rebuild the Workspace (Intentional Project Load, rather than UI context switch).
        if ((!Initialised) || (NavigationCacheMode == NavigationCacheMode.Disabled))
        {
            var leftPanelWidth = ViewModel.ContextPanelWidth;
            var rightPanelWidth = ViewModel.InspectorPanelWidth;
            var bottomPanelHeight = ViewModel.ConsolePanelHeight;

            if (leftPanelWidth > 0)
            {
                ContextPanelColumn.Width = new GridLength(leftPanelWidth);
            }
            if (rightPanelWidth > 0)
            {
                InspectorPanelColumn.Width = new GridLength(rightPanelWidth);
            }
            if (bottomPanelHeight > 0)
            {
                ConsolePanelRow.Height = new GridLength(bottomPanelHeight);
            }

            UpdatePanels();

            ContextPanel.SizeChanged += (s, e) => ViewModel.ContextPanelWidth = (float)e.NewSize.Width;
            InspectorPanel.SizeChanged += (s, e) => ViewModel.InspectorPanelWidth = (float)e.NewSize.Width;
            ConsolePanel.SizeChanged += (s, e) => ViewModel.ConsolePanelHeight = (float)e.NewSize.Height;

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            //
            // Populate the workspace panels.
            //

            var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
            var workspaceService = workspaceWrapper.WorkspaceService as WorkspaceService;
            Guard.IsNotNull(workspaceService);

            // Send a message to tell services to initialize their workspace panels
            var message = new WorkspaceWillPopulatePanelsMessage();
            _messengerService.Send(message);

            // Populate the context panel with explorer and search panels
            var explorerPanel = workspaceService.ExplorerService.ExplorerPanel as UIElement;
            if (explorerPanel != null)
            {
                workspaceService.AddContextAreaUse(ContextAreaUse.Explorer, explorerPanel);
                ContextPanel.Children.Insert(0, explorerPanel);
            }

            //        var searchPanel = workspaceService.SearchService.SearchPanel as UIElement;
            var searchPanel = workspaceService.ExplorerService.SearchPanel as UIElement;
            if (searchPanel != null)
            {
                workspaceService.AddContextAreaUse(ContextAreaUse.Search, searchPanel);
                ContextPanel.Children.Insert(1, searchPanel);
            }
            /*
            var debugPanel = workspaceService.DebugService.DebugPanel as UIElement;
            workspaceService.AddContextAreaUse(IWorkspaceService.ContextAreaUse.Debug, debugPanel);
            ContextPanel.Children.Insert(2, debugPanel);

            var revisionControlPanel = workspaceService.RevisionControlService.RevisioncControlPanel as UIElement;
            workspaceService.AddContextAreaUse(IWorkspaceService.ContextAreaUse.VersionControl, revisionControlPanel);
            ContextPanel.Children.Insert(3, revisionControlPanel);
            */
            var documentsPanel = workspaceService.DocumentsService.DocumentsPanel as UIElement;
            DocumentsPanel.Children.Add(documentsPanel);

            var inspectorPanel = workspaceService.InspectorService.InspectorPanel as UIElement;
            InspectorPanel.Children.Add(inspectorPanel);

            var consolePanel = workspaceService.ConsoleService.ConsolePanel as UIElement;
            ConsolePanel.Children.Add(consolePanel);

            var statusPanel = workspaceService.StatusService.StatusPanel as UIElement;
            StatusPanel.Children.Add(statusPanel);

            workspaceService.SetCurrentContextAreaUsage(ContextAreaUse.Explorer);

            _ = ViewModel.LoadWorkspaceAsync();

            Initialised = true;
        }

        // Reset our cache status to required.
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    private void WorkspacePage_Unloaded(object sender, RoutedEventArgs e)
    {
    }

    public override void PageUnloadInternal()
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        var workspaceService = workspaceWrapper.WorkspaceService as WorkspaceService;
        Guard.IsNotNull(workspaceService);

        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

        // Close all open documents and clean up their WebView2 resources
        var documentsPanel = workspaceService.DocumentsService.DocumentsPanel;
        documentsPanel?.Shutdown();

        // Clean up WebView2 resources in the ConsolePanel
        var consolePanel = workspaceService.ConsoleService.ConsolePanel as ConsolePanel;
        consolePanel?.Shutdown();

        ViewModel.OnWorkspacePageUnloaded();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.IsContextPanelVisible):
            case nameof(ViewModel.IsInspectorPanelVisible):
            case nameof(ViewModel.IsConsolePanelVisible):
                UpdatePanels();
                break;
        }
    }

    private void UpdatePanels()
    {
        //
        // Update panel and splitter visibility based on the panel visibility state
        //

        if (ViewModel.IsContextPanelVisible)
        {
            ContextPanelSplitter.Visibility = Visibility.Visible;
            ContextPanel.Visibility = Visibility.Visible;
            ContextPanelColumn.MinWidth = 100;
            ContextPanelColumn.Width = new GridLength(ViewModel.ContextPanelWidth);
        }
        else
        {
            ContextPanelSplitter.Visibility = Visibility.Collapsed;
            ContextPanel.Visibility = Visibility.Collapsed;
            ContextPanelColumn.MinWidth = 0;
            ContextPanelColumn.Width = new GridLength(0);
        }

        if (ViewModel.IsInspectorPanelVisible)
        {
            InspectorPanelSplitter.Visibility = Visibility.Visible;
            InspectorPanel.Visibility = Visibility.Visible;
            InspectorPanelColumn.MinWidth = 100;
            InspectorPanelColumn.Width = new GridLength(ViewModel.InspectorPanelWidth);
        }
        else
        {
            InspectorPanelSplitter.Visibility = Visibility.Collapsed;
            InspectorPanel.Visibility = Visibility.Collapsed;
            InspectorPanelColumn.MinWidth = 0;
            InspectorPanelColumn.Width = new GridLength(0);
        }

        if (ViewModel.IsConsolePanelVisible)
        {
            ConsolePanelSplitter.Visibility = Visibility.Visible;
            ConsolePanel.Visibility = Visibility.Visible;
            ConsolePanelRow.MinHeight = 100;
            ConsolePanelRow.Height = new GridLength(ViewModel.ConsolePanelHeight);
        }
        else
        {
            ConsolePanelSplitter.Visibility = Visibility.Collapsed;
            ConsolePanel.Visibility = Visibility.Collapsed;
            ConsolePanelRow.MinHeight = 0;
            ConsolePanelRow.Height = new GridLength(0);
        }
    }

    private void Panel_GotFocus(object sender, RoutedEventArgs e)
    {
        FrameworkElement? frameworkElement = sender as FrameworkElement;
        if (frameworkElement is not null)
        {
            var panelName = frameworkElement?.Name;
            if (!string.IsNullOrEmpty(panelName))
            {
                SetActivePanel(panelName);
            }
        }
    }

    private void Panel_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        FrameworkElement? frameworkElement = sender as FrameworkElement;
        if (frameworkElement is not null)
        {
            var panelName = frameworkElement?.Name;
            if (!string.IsNullOrEmpty(panelName))
            {
                SetActivePanel(panelName);
            }
        }
    }

    private void SetActivePanel(string panelName)
    {
        string trimmed = panelName.Replace("Panel", string.Empty);
        if (Enum.TryParse<WorkspacePanel>(trimmed, out var panel))
        {
            ViewModel.SetActivePanel(panel);
        }
    }
}
