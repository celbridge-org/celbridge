using Celbridge.Messaging;
using Celbridge.Navigation;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.Workspace.ViewModels;
using Celbridge.Console.Views;

namespace Celbridge.Workspace.Views;

public sealed partial class WorkspacePage : Page
{
    private readonly IMessengerService _messengerService;
    private readonly INavigationService _navigationService;
    private readonly IEditorSettings _editorSettings;

    public WorkspacePageViewModel ViewModel { get; }

    private bool _initialized = false;
    private ProjectPanel? _projectPanel;
    private UIElement? _inspectorPanel;

    public WorkspacePage()
    {
        InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<WorkspacePageViewModel>();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _navigationService = ServiceLocator.AcquireService<INavigationService>();
        _editorSettings = ServiceLocator.AcquireService<IEditorSettings>();

        DataContext = ViewModel;

        // Enable caching so the page persists during navigation
        NavigationCacheMode = NavigationCacheMode.Required;

        Loaded += WorkspacePage_Loaded;
        Unloaded += WorkspacePage_Unloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.LoadProjectCancellationToken = e.Parameter as CancellationTokenSource;
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        // Check if cleanup was requested before navigation occurs
        if (_navigationService.IsWorkspacePageCleanupPending)
        {
            // Disable caching so the page will be cleaned up on unload
            NavigationCacheMode = NavigationCacheMode.Disabled;
        }

        base.OnNavigatingFrom(e);
    }

    private void WorkspacePage_Loaded(object sender, RoutedEventArgs e)
    {
        // Only execute initialization if this is the first load or if we're rebuilding after cache clear
        if (!_initialized || NavigationCacheMode == NavigationCacheMode.Disabled)
        {
            InitializeWorkspace();
            _initialized = true;

            // Re-enable caching after initialization
            NavigationCacheMode = NavigationCacheMode.Required;
        }
    }

    private void InitializeWorkspace()
    {
        var primaryPanelWidth = ViewModel.PrimaryPanelWidth;
        var secondaryPanelWidth = ViewModel.SecondaryPanelWidth;
        var bottomPanelHeight = ViewModel.ConsolePanelHeight;

        if (primaryPanelWidth > 0)
        {
            PrimaryPanelColumn.Width = new GridLength(primaryPanelWidth);
        }
        if (secondaryPanelWidth > 0)
        {
            SecondaryPanelColumn.Width = new GridLength(secondaryPanelWidth);
        }
        if (bottomPanelHeight > 0)
        {
            ConsolePanelRow.Height = new GridLength(bottomPanelHeight);
        }

        UpdatePanels();

        PrimaryPanel.SizeChanged += (s, e) => ViewModel.PrimaryPanelWidth = (float)e.NewSize.Width;
        SecondaryPanel.SizeChanged += (s, e) => ViewModel.SecondaryPanelWidth = (float)e.NewSize.Width;
        ConsolePanel.SizeChanged += (s, e) => ViewModel.ConsolePanelHeight = (float)e.NewSize.Height;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Register for messages
        _messengerService.Register<PanelSwapChangedMessage>(this, OnPanelSwapChanged);

        //
        // Populate the workspace panels.
        //

        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        var workspaceService = workspaceWrapper.WorkspaceService;
        Guard.IsNotNull(workspaceService);

        // Send a message to tell services to initialize their workspace panels
        var message = new WorkspaceWillPopulatePanelsMessage();
        _messengerService.Send(message);

        // Create the ProjectPanel
        _projectPanel = new ProjectPanel();

        // Get the explorer and search panels from the workspace service
        var explorerPanel = workspaceService.ExplorerService.ExplorerPanel as UIElement;
        var searchPanel = workspaceService.ExplorerService.SearchPanel as UIElement;

        if (explorerPanel != null && searchPanel != null)
        {
            // Register panels with the project panel service for view management
            workspaceService.ProjectPanelService.RegisterView(ProjectPanelView.Explorer, explorerPanel);
            workspaceService.ProjectPanelService.RegisterView(ProjectPanelView.Search, searchPanel);

            // Populate the ProjectPanel with explorer and search panels
            _projectPanel.PopulatePanels(explorerPanel, searchPanel);
        }

        var documentsPanel = workspaceService.DocumentsService.DocumentsPanel as UIElement;
        DocumentsPanel.Children.Add(documentsPanel);

        _inspectorPanel = workspaceService.InspectorService.InspectorPanel as UIElement;

        var consolePanel = workspaceService.ConsoleService.ConsolePanel as UIElement;
        ConsolePanel.Children.Add(consolePanel);

        // Apply initial panel swap setting
        ApplyPanelSwap(_editorSettings.SwapPrimarySecondaryPanels);

        // Show the Explorer view by default
        workspaceService.ProjectPanelService.ShowView(ProjectPanelView.Explorer);

        _ = ViewModel.LoadWorkspaceAsync();
    }

    private void WorkspacePage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Only perform cleanup if the cache has been disabled (intentional unload)
        if (NavigationCacheMode == NavigationCacheMode.Disabled)
        {
            PerformCleanup();
        }
    }

    private void PerformCleanup()
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        var workspaceService = workspaceWrapper.WorkspaceService;
        Guard.IsNotNull(workspaceService);

        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

        // Unregister message handlers
        _messengerService.Unregister<PanelSwapChangedMessage>(this);

        // Close all open documents and clean up their WebView2 resources
        var documentsPanel = workspaceService.DocumentsService.DocumentsPanel;
        documentsPanel?.Shutdown();

        // Clean up WebView2 resources in the ConsolePanel
        var consolePanel = workspaceService.ConsoleService.ConsolePanel as ConsolePanel;
        consolePanel?.Shutdown();

        ViewModel.OnWorkspacePageUnloaded();

        _initialized = false;
    }

    private void OnPanelSwapChanged(object recipient, PanelSwapChangedMessage message)
    {
        ApplyPanelSwap(message.IsSwapped);
    }

    private void ApplyPanelSwap(bool isSwapped)
    {
        // Remove panels from their current containers
        PrimaryPanel.Children.Clear();
        SecondaryPanel.Children.Clear();

        if (isSwapped)
        {
            // Swapped: Inspector in Primary, ProjectPanel in Secondary
            if (_inspectorPanel != null)
            {
                PrimaryPanel.Children.Add(_inspectorPanel);
            }
            if (_projectPanel != null)
            {
                SecondaryPanel.Children.Add(_projectPanel);
            }
        }
        else
        {
            // Default: ProjectPanel in Primary, Inspector in Secondary
            if (_projectPanel != null)
            {
                PrimaryPanel.Children.Add(_projectPanel);
            }
            if (_inspectorPanel != null)
            {
                SecondaryPanel.Children.Add(_inspectorPanel);
            }
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.IsPrimaryPanelVisible):
            case nameof(ViewModel.IsSecondaryPanelVisible):
            case nameof(ViewModel.IsConsolePanelVisible):
                UpdatePanels();
                break;
            case nameof(ViewModel.PrimaryPanelWidth):
                if (ViewModel.IsPrimaryPanelVisible && ViewModel.PrimaryPanelWidth > 0)
                {
                    PrimaryPanelColumn.Width = new GridLength(ViewModel.PrimaryPanelWidth);
                }
                break;
            case nameof(ViewModel.SecondaryPanelWidth):
                if (ViewModel.IsSecondaryPanelVisible && ViewModel.SecondaryPanelWidth > 0)
                {
                    SecondaryPanelColumn.Width = new GridLength(ViewModel.SecondaryPanelWidth);
                }
                break;
            case nameof(ViewModel.ConsolePanelHeight):
                if (ViewModel.IsConsolePanelVisible && ViewModel.ConsolePanelHeight > 0)
                {
                    ConsolePanelRow.Height = new GridLength(ViewModel.ConsolePanelHeight);
                }
                break;
        }
    }

    private void UpdatePanels()
    {
        //
        // Update panel and splitter visibility based on the panel visibility state
        //

        if (ViewModel.IsPrimaryPanelVisible)
        {
            PrimaryPanelSplitter.Visibility = Visibility.Visible;
            PrimaryPanel.Visibility = Visibility.Visible;
            PrimaryPanelColumn.MinWidth = 100;
            PrimaryPanelColumn.Width = new GridLength(ViewModel.PrimaryPanelWidth);
        }
        else
        {
            PrimaryPanelSplitter.Visibility = Visibility.Collapsed;
            PrimaryPanel.Visibility = Visibility.Collapsed;
            PrimaryPanelColumn.MinWidth = 0;
            PrimaryPanelColumn.Width = new GridLength(0);
        }

        if (ViewModel.IsSecondaryPanelVisible)
        {
            SecondaryPanelSplitter.Visibility = Visibility.Visible;
            SecondaryPanel.Visibility = Visibility.Visible;
            SecondaryPanelColumn.MinWidth = 100;
            SecondaryPanelColumn.Width = new GridLength(ViewModel.SecondaryPanelWidth);
        }
        else
        {
            SecondaryPanelSplitter.Visibility = Visibility.Collapsed;
            SecondaryPanel.Visibility = Visibility.Collapsed;
            SecondaryPanelColumn.MinWidth = 0;
            SecondaryPanelColumn.Width = new GridLength(0);
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
        if (sender is FrameworkElement frameworkElement && 
            frameworkElement.Tag is string panelTag &&
            !string.IsNullOrEmpty(panelTag))
        {
            SetActivePanel(panelTag);
        }
    }

    private void Panel_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement frameworkElement && 
            frameworkElement.Tag is string panelTag &&
            !string.IsNullOrEmpty(panelTag))
        {
            SetActivePanel(panelTag);
        }
    }

    private void SetActivePanel(string panelTag)
    {
        if (!Enum.TryParse<WorkspacePanel>(panelTag, out var panel))
        {
            throw new ArgumentException($"Invalid panel tag: '{panelTag}'. Tag must match a WorkspacePanel enum value.");
        }

        ViewModel.SetActivePanel(panel);
    }
}
