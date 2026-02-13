using Celbridge.Console;
using Celbridge.Console.Views;
using Celbridge.Documents;
using Celbridge.Inspector;
using Celbridge.Navigation;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace.ViewModels;

namespace Celbridge.Workspace.Views;

public sealed partial class WorkspacePage : Page
{
    private readonly INavigationService _navigationService;

    public WorkspacePageViewModel ViewModel { get; }

    private bool _initialized = false;

    private SplitterHelper? _primaryPanelSplitterHelper;
    private SplitterHelper? _secondaryPanelSplitterHelper;
    private SplitterHelper? _consolePanelSplitterHelper;

    public WorkspacePage()
    {
        InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<WorkspacePageViewModel>();

        _navigationService = ServiceLocator.AcquireService<INavigationService>();

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

        // Initialize splitter helpers
        _primaryPanelSplitterHelper = new SplitterHelper(LayoutRoot, GridResizeMode.Columns, 0, minSize: 100);
        _secondaryPanelSplitterHelper = new SplitterHelper(LayoutRoot, GridResizeMode.Columns, 2, minSize: 100, invertDelta: true);
        _consolePanelSplitterHelper = new SplitterHelper(LayoutRoot, GridResizeMode.Rows, 1, minSize: 100, invertDelta: true);

        // Set up splitter event handlers
        PrimaryPanelSplitter.DragStarted += PrimaryPanelSplitter_DragStarted;
        PrimaryPanelSplitter.DragDelta += PrimaryPanelSplitter_DragDelta;
        PrimaryPanelSplitter.DoubleClicked += PrimaryPanelSplitter_DoubleClicked;

        SecondaryPanelSplitter.DragStarted += SecondaryPanelSplitter_DragStarted;
        SecondaryPanelSplitter.DragDelta += SecondaryPanelSplitter_DragDelta;
        SecondaryPanelSplitter.DoubleClicked += SecondaryPanelSplitter_DoubleClicked;

        ConsolePanelSplitter.DragStarted += ConsolePanelSplitter_DragStarted;
        ConsolePanelSplitter.DragDelta += ConsolePanelSplitter_DragDelta;
        ConsolePanelSplitter.DoubleClicked += ConsolePanelSplitter_DoubleClicked;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        //
        // Populate the workspace panels.
        //

        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        var workspaceService = workspaceWrapper.WorkspaceService;
        Guard.IsNotNull(workspaceService);

        // Create panels via DI
        var activityPanel = ServiceLocator.AcquireService<IActivityPanel>();
        var documentsPanel = ServiceLocator.AcquireService<IDocumentsPanel>();
        var inspectorPanel = ServiceLocator.AcquireService<IInspectorPanel>();
        var consolePanel = ServiceLocator.AcquireService<IConsolePanel>();

        // Register panels with the workspace service
        workspaceService.SetPanels(activityPanel, documentsPanel, inspectorPanel, consolePanel);

        // Add panels to the UI
        PrimaryPanel.Children.Add(activityPanel as UIElement);
        DocumentsPanel.Children.Add(documentsPanel as UIElement);
        SecondaryPanel.Children.Add(inspectorPanel as UIElement);
        ConsolePanel.Children.Add(consolePanel as UIElement);

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

        // Close all open documents and clean up their WebView2 resources
        workspaceService.DocumentsPanel.Shutdown();

        if (workspaceService.ConsolePanel is ConsolePanel consolePanel)
        {
            consolePanel.Shutdown();
        }

        ViewModel.OnWorkspacePageUnloaded();

        _initialized = false;
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

    //
    // Splitter event handlers for panel resizing
    //

    private void PrimaryPanelSplitter_DragStarted(object? sender, EventArgs e)
    {
        _primaryPanelSplitterHelper?.OnDragStarted();
    }

    private void PrimaryPanelSplitter_DragDelta(object? sender, double delta)
    {
        _primaryPanelSplitterHelper?.OnDragDelta(delta);
    }

    private void SecondaryPanelSplitter_DragStarted(object? sender, EventArgs e)
    {
        _secondaryPanelSplitterHelper?.OnDragStarted();
    }

    private void SecondaryPanelSplitter_DragDelta(object? sender, double delta)
    {
        _secondaryPanelSplitterHelper?.OnDragDelta(delta);
    }

    private void ConsolePanelSplitter_DragStarted(object? sender, EventArgs e)
    {
        _consolePanelSplitterHelper?.OnDragStarted();
    }

    private void ConsolePanelSplitter_DragDelta(object? sender, double delta)
    {
        _consolePanelSplitterHelper?.OnDragDelta(delta);
    }

    private void PrimaryPanelSplitter_DoubleClicked(object? sender, EventArgs e)
    {
        ViewModel.PrimaryPanelWidth = UserInterfaceConstants.PrimaryPanelWidth;
    }

    private void SecondaryPanelSplitter_DoubleClicked(object? sender, EventArgs e)
    {
        ViewModel.SecondaryPanelWidth = UserInterfaceConstants.SecondaryPanelWidth;
    }

    private void ConsolePanelSplitter_DoubleClicked(object? sender, EventArgs e)
    {
        ViewModel.ConsolePanelHeight = UserInterfaceConstants.ConsolePanelHeight;
    }
}
