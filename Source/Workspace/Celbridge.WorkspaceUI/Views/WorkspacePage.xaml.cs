using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Platform;
using Celbridge.Settings;
using Celbridge.UserInterface.DragDrop;
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

    private readonly ICommandService _commandService;
    private readonly Logging.ILogger<WorkspacePage> _logger;
    private readonly IPlatformInfo _platformInfo;
    private readonly IResourceDragCoordinator _resourceDragCoordinator;

    public WorkspacePageViewModel ViewModel { get; }

    private bool _initialized = false;
    private readonly IFeatureFlags _featureFlags;

    private SplitterHelper? _primaryPanelSplitterHelper;
    private SplitterHelper? _secondaryPanelSplitterHelper;
    private SplitterHelper? _consolePanelSplitterHelper;

    // The project-notification banner strip, kept so its messenger subscriptions can be torn down on
    // page unload.
    private NotificationBar? _notificationBar;

    public WorkspacePage()
    {
        InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<WorkspacePageViewModel>();

        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _logger = ServiceLocator.AcquireService<Logging.ILogger<WorkspacePage>>();
        _featureFlags = ServiceLocator.AcquireService<IFeatureFlags>();
        _platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        _resourceDragCoordinator = ServiceLocator.AcquireService<IResourceDragCoordinator>();

        DataContext = ViewModel;

        // Enable caching so the page persists during navigation
        NavigationCacheMode = NavigationCacheMode.Required;

        Loaded += WorkspacePage_Loaded;
        Unloaded += WorkspacePage_Unloaded;
    }

    private async void WorkspacePage_Loaded(object sender, RoutedEventArgs e)
    {
        // Only execute initialization if this is the first load or if we're rebuilding after cache clear
        if (_initialized && NavigationCacheMode != NavigationCacheMode.Disabled)
        {
            return;
        }

        // Mark initialized and restore caching up front so a second Loaded event
        // raised during the awaited settings load cannot start a duplicate
        // initialization.
        _initialized = true;
        NavigationCacheMode = NavigationCacheMode.Required;

        // Read the navigation parameter passed via Page.Tag by the navigation system
        ViewModel.LoadProjectCancellationToken = Tag as CancellationTokenSource;

        // Bring the per-project store online before the panels bind, so they restore
        // this project's panel sizes instead of racing the asynchronous workspace load.
        var acquireResult = await ViewModel.AcquireWorkspaceSettingsAsync();
        if (acquireResult.IsFailure)
        {
            _logger.LogWarning(acquireResult, "Failed to acquire workspace settings before initializing panels");
        }

        InitializeWorkspace();
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

        ConsolePanelHost.SizeChanged += (s, e) =>
        {
            ViewModel.ConsolePanelHeight = (float)e.NewSize.Height;
        };

        // Initialize splitter helpers
        _primaryPanelSplitterHelper = new SplitterHelper(LayoutRoot, GridResizeMode.Columns, 0, minSize: MinSidePanelWidth,
            maxSizeFunc: () => LayoutRoot.ActualWidth - SecondaryPanelColumn.ActualWidth - MinDocumentsWidth);
        _secondaryPanelSplitterHelper = new SplitterHelper(LayoutRoot, GridResizeMode.Columns, 2, minSize: MinSidePanelWidth, invertDelta: true,
            maxSizeFunc: () => LayoutRoot.ActualWidth - PrimaryPanelColumn.ActualWidth - MinDocumentsWidth);
        _consolePanelSplitterHelper = new SplitterHelper(LayoutRoot, GridResizeMode.Rows, 1, minSize: MinConsolePanelHeight, invertDelta: true,
            maxSizeFunc: () => LayoutRoot.ActualHeight - NotificationBarHost.ActualHeight - MinDocumentsHeight);

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

        // Populate the workspace panels.
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        var workspaceService = workspaceWrapper.WorkspaceService;
        Guard.IsNotNull(workspaceService);

        var isConsolePanelEnabled = _featureFlags.IsEnabled(FeatureFlagConstants.ConsolePanel);

        // Create panels via DI
        var utilityPanel = ServiceLocator.AcquireService<IUtilityPanel>();
        var documentsPanel = ServiceLocator.AcquireService<IDocumentsPanel>();

        if (!isConsolePanelEnabled)
        {
            // Hide console panel row and splitter completely when feature is disabled
            ConsolePanelRow.Height = new GridLength(0);
            ConsolePanelRow.MinHeight = 0;
            ConsolePanelSplitter.Visibility = Visibility.Collapsed;
            ConsolePanelHost.Visibility = Visibility.Collapsed;
        }

        // The notification bar is not a layout region, so it is always present and is not gated on
        // the console panel feature flag. It collapses to zero height when no banners are showing.
        _notificationBar = ServiceLocator.AcquireService<NotificationBar>();
        NotificationBarHost.Children.Add(_notificationBar);

        // Register panels with the workspace service
        workspaceService.SetPanels(utilityPanel, documentsPanel);

        // Add panels to the UI
        PrimaryPanel.Children.Add(utilityPanel as UIElement);
        DocumentsPanel.Children.Add(documentsPanel as UIElement);

        // Enable the pointer-driven resource drag overlay on heads where the built-in drag-and-drop is
        // disabled. The panels register their drop targets with the coordinator as they load.
        if (_platformInfo.UsesPointerDrivenTabDrag)
        {
            _resourceDragCoordinator.Initialize(ResourceDragOverlay, LayoutRoot);
        }

        // Listen for workspace loaded message and feature flag changes to update console visibility
        var messengerService = ServiceLocator.AcquireService<IMessengerService>();
        messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
        messengerService.Register<FeatureFlagsChangedMessage>(this, OnFeatureFlagsChanged);

        _ = ViewModel.LoadWorkspaceAsync();
    }

    private async void WorkspacePage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Only perform cleanup if the cache has been disabled (intentional unload)
        if (NavigationCacheMode == NavigationCacheMode.Disabled)
        {
            await PerformCleanupAsync();
        }
    }

    private async Task PerformCleanupAsync()
    {
        // Cleanup owned by this page: its own view-model and message subscriptions. The workspace teardown
        // (save editor state, shut down panels, dispose the workspace) is orchestrated by the view-model.
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

        // Unbind the shared resource drag coordinator from this workspace. Safe when it was never
        // initialized (heads without the drag overlay).
        _resourceDragCoordinator.Reset();

        var messengerService = ServiceLocator.AcquireService<IMessengerService>();
        messengerService.UnregisterAll(this);

        // Tear down the notification bar's messenger subscriptions.
        _notificationBar?.Cleanup();
        _notificationBar = null;

        await ViewModel.OnWorkspacePageUnloadedAsync();

        _initialized = false;
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        // Update console panel visibility now that workspace has loaded with potentially different feature flag settings
        UpdatePanels();
    }

    private void OnFeatureFlagsChanged(object recipient, FeatureFlagsChangedMessage message)
    {
        UpdatePanels();
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
                if (ViewModel.IsConsolePanelVisible &&
                    ViewModel.ConsolePanelHeight > 0)
                {
                    ConsolePanelRow.Height = new GridLength(ViewModel.ConsolePanelHeight);
                }
                break;
        }
    }

    private void UpdatePanels()
    {
        // Update panel and splitter visibility based on the panel visibility state
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

        var isConsolePanelEnabled = _featureFlags.IsEnabled(FeatureFlagConstants.ConsolePanel);

        if (isConsolePanelEnabled && ViewModel.IsConsolePanelVisible)
        {
            ConsolePanelSplitter.Visibility = Visibility.Visible;
            ConsolePanelHost.Visibility = Visibility.Visible;
            ConsolePanelRow.MinHeight = MinConsolePanelHeight;
            ConsolePanelRow.Height = new GridLength(ViewModel.ConsolePanelHeight);
        }
        else
        {
            ConsolePanelSplitter.Visibility = Visibility.Collapsed;
            ConsolePanelHost.Visibility = Visibility.Collapsed;
            ConsolePanelRow.MinHeight = 0;
            ConsolePanelRow.Height = new GridLength(0);
        }
    }

    // Splitter event handlers for panel resizing
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
