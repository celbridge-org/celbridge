using Celbridge.Commands;
using Celbridge.Console;
using Celbridge.Documents;
using Celbridge.Inspector;
using Celbridge.UserInterface.Helpers;
using Celbridge.WorkspaceUI.ViewModels;

namespace Celbridge.WorkspaceUI.Views;

public sealed partial class WorkspacePage : Page
{
    // Minimum height for the documents area when resizing the console panel
    private const double MinDocumentsHeight = 150;

    // Minimum width for the documents area when resizing side panels
    private const double MinDocumentsWidth = 200;

    // Minimum width for side panels
    private const double MinSidePanelWidth = 200;

    // Minimum height for the console panel
    private const double MinConsolePanelHeight = 150;

    // Maximum fraction of available vertical space for restored console height
    private const double MaxRestoredConsoleHeightFraction = 0.7;

    private readonly ICommandService _commandService;

    public WorkspacePageViewModel ViewModel { get; }

    private bool _initialized = false;

    private SplitterHelper? _primaryPanelSplitterHelper;
    private SplitterHelper? _secondaryPanelSplitterHelper;
    private SplitterHelper? _consolePanelSplitterHelper;

    public WorkspacePage()
    {
        InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<WorkspacePageViewModel>();

        _commandService = ServiceLocator.AcquireService<ICommandService>();

        DataContext = ViewModel;

        // Enable caching so the page persists during navigation
        NavigationCacheMode = NavigationCacheMode.Required;

        Loaded += WorkspacePage_Loaded;
        Unloaded += WorkspacePage_Unloaded;
    }

    private void WorkspacePage_Loaded(object sender, RoutedEventArgs e)
    {
        // Only execute initialization if this is the first load or if we're rebuilding after cache clear
        if (!_initialized || NavigationCacheMode == NavigationCacheMode.Disabled)
        {
            // Read the navigation parameter passed via Page.Tag by the navigation system
            ViewModel.LoadProjectCancellationToken = Tag as CancellationTokenSource;

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

        // Only update console height setting when not maximized (to preserve restore height)
        ConsolePanel.SizeChanged += (s, e) =>
        {
            if (!ViewModel.IsConsoleMaximized)
            {
                ViewModel.ConsolePanelHeight = (float)e.NewSize.Height;
            }
        };

        // Initialize splitter helpers
        _primaryPanelSplitterHelper = new SplitterHelper(LayoutRoot, GridResizeMode.Columns, 0, minSize: MinSidePanelWidth,
            maxSizeFunc: () => LayoutRoot.ActualWidth - SecondaryPanelColumn.ActualWidth - MinDocumentsWidth);
        _secondaryPanelSplitterHelper = new SplitterHelper(LayoutRoot, GridResizeMode.Columns, 2, minSize: MinSidePanelWidth, invertDelta: true,
            maxSizeFunc: () => LayoutRoot.ActualWidth - PrimaryPanelColumn.ActualWidth - MinDocumentsWidth);
        _consolePanelSplitterHelper = new SplitterHelper(LayoutRoot, GridResizeMode.Rows, 1, minSize: MinConsolePanelHeight, invertDelta: true,
            maxSizeFunc: () => LayoutRoot.ActualHeight - MinDocumentsHeight);

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
        workspaceService.ConsolePanel.Shutdown();

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

            case nameof(ViewModel.IsConsoleMaximized):
                UpdateConsoleMaximized();
                break;

            case nameof(ViewModel.PrimaryPanelWidth):
                if (ViewModel.IsPrimaryPanelVisible && 
                    ViewModel.PrimaryPanelWidth > 0)
                {
                    PrimaryPanelColumn.Width = new GridLength(ViewModel.PrimaryPanelWidth);
                }
                break;

            case nameof(ViewModel.SecondaryPanelWidth):
                if (ViewModel.IsSecondaryPanelVisible && 
                    ViewModel.SecondaryPanelWidth > 0)
                {
                    SecondaryPanelColumn.Width = new GridLength(ViewModel.SecondaryPanelWidth);
                }
                break;

            case nameof(ViewModel.ConsolePanelHeight):
                // Don't update row height when console is maximized (it uses Star sizing)
                if (ViewModel.IsConsolePanelVisible && 
                    ViewModel.ConsolePanelHeight > 0 && 
                    !ViewModel.IsConsoleMaximized)
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
            PrimaryPanelColumn.MinWidth = MinSidePanelWidth;
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
            SecondaryPanelColumn.MinWidth = MinSidePanelWidth;
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
            ConsolePanelRow.MinHeight = MinConsolePanelHeight;
            ConsolePanelRow.Height = new GridLength(ViewModel.ConsolePanelHeight);
        }
        else
        {
            ConsolePanelSplitter.Visibility = Visibility.Collapsed;
            ConsolePanel.Visibility = Visibility.Collapsed;
            ConsolePanelRow.MinHeight = 0;
            ConsolePanelRow.Height = new GridLength(0);
        }

        // Apply console maximized state after panel visibility
        UpdateConsoleMaximized();
    }

    private void UpdateConsoleMaximized()
    {
        if (!ViewModel.IsConsolePanelVisible)
        {
            // Console is hidden, nothing to maximize
            return;
        }

        if (ViewModel.IsConsoleMaximized)
        {
            // Hide the splitter while maximized
            ConsolePanelSplitter.Visibility = Visibility.Collapsed;

            // Hide Documents panel and row
            DocumentsPanel.Visibility = Visibility.Collapsed;
            DocumentsPanelRow.MinHeight = 0;
            DocumentsPanelRow.Height = new GridLength(0);

            // Maximize Console row using Star sizing so it fills available space
            ConsolePanelRow.MinHeight = 0;
            ConsolePanelRow.Height = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            // Show the splitter when restored
            ConsolePanelSplitter.Visibility = Visibility.Visible;

            // Restore Documents panel and row
            DocumentsPanel.Visibility = Visibility.Visible;
            DocumentsPanelRow.MinHeight = 0;
            DocumentsPanelRow.Height = new GridLength(1, GridUnitType.Star);

            // Restore console MinHeight
            ConsolePanelRow.MinHeight = MinConsolePanelHeight;

            // Restore console to the height it was before maximizing
            var consoleHeight = ViewModel.ConsolePanelHeight;
            if (consoleHeight <= 0)
            {
                consoleHeight = WorkspaceConstants.ConsolePanelHeight;
            }

            // Clamp to max fraction of available height to ensure documents area is visible.
            // This handles the case where the window was resized smaller while console was maximized.
            var maxConsoleHeight = (float)(LayoutRoot.ActualHeight * MaxRestoredConsoleHeightFraction);
            if (consoleHeight > maxConsoleHeight && maxConsoleHeight > 100)
            {
                consoleHeight = maxConsoleHeight;
            }

            // Set Console row to fixed height
            ConsolePanelRow.Height = new GridLength(consoleHeight);
        }

        // Force layout recalculation
        LayoutRoot.InvalidateMeasure();
        LayoutRoot.InvalidateArrange();
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
        _commandService.Execute<IResetPanelCommand>(command =>
        {
            command.Region = LayoutRegion.Primary;
        });
    }

    private void SecondaryPanelSplitter_DoubleClicked(object? sender, EventArgs e)
    {
        _commandService.Execute<IResetPanelCommand>(command =>
        {
            command.Region = LayoutRegion.Secondary;
        });
    }

    private void ConsolePanelSplitter_DoubleClicked(object? sender, EventArgs e)
    {
        _commandService.Execute<IResetPanelCommand>(command =>
        {
            command.Region = LayoutRegion.Console;
        });
    }
}
