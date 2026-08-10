using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Platform;
using Celbridge.UserInterface.DragDrop;
using Celbridge.UserInterface.Helpers;
using Celbridge.WorkspaceUI.ViewModels;

namespace Celbridge.WorkspaceUI.Views;

public sealed partial class WorkspacePage : Page
{
    // Minimum width for the Documents panel when resizing the Utility Panel
    private const double MinDocumentsWidth = 200;

    // Minimum width for the Utility Panel
    private const double MinUtilityPanelWidth = 200;

    private readonly ICommandService _commandService;
    private readonly Logging.ILogger<WorkspacePage> _logger;
    private readonly IPlatformInfo _platformInfo;
    private readonly IResourceDragCoordinator _resourceDragCoordinator;

    public WorkspacePageViewModel ViewModel { get; }

    private bool _initialized = false;

    private SplitterHelper? _utilityPanelSplitterHelper;

    // The project-notification banner strip, kept so its messenger subscriptions can be torn down on
    // page unload.
    private NotificationBar? _notificationBar;

    public WorkspacePage()
    {
        InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<WorkspacePageViewModel>();

        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _logger = ServiceLocator.AcquireService<Logging.ILogger<WorkspacePage>>();
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
        var utilityPanelWidth = ViewModel.UtilityPanelWidth;
        if (utilityPanelWidth > 0)
        {
            UtilityPanelColumn.Width = new GridLength(utilityPanelWidth);
        }

        UpdatePanels();

        UtilityPanelHost.SizeChanged += (s, e) => ViewModel.UtilityPanelWidth = (float)e.NewSize.Width;

        _utilityPanelSplitterHelper = new SplitterHelper(LayoutRoot, GridResizeMode.Columns, 0, minSize: MinUtilityPanelWidth,
            maxSizeFunc: () => LayoutRoot.ActualWidth - MinDocumentsWidth);

        UtilityPanelSplitter.DragStarted += UtilityPanelSplitter_DragStarted;
        UtilityPanelSplitter.DragDelta += UtilityPanelSplitter_DragDelta;
        UtilityPanelSplitter.DoubleClicked += UtilityPanelSplitter_DoubleClicked;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Populate the workspace panels.
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        var workspaceService = workspaceWrapper.WorkspaceService;
        Guard.IsNotNull(workspaceService);

        // Create panels via DI
        var utilityPanel = ServiceLocator.AcquireService<IUtilityPanel>();
        var documentsPanel = ServiceLocator.AcquireService<IDocumentsPanel>();

        // The notification bar is not a layout surface, so it is always present. It collapses to zero
        // height when no banners are showing.
        _notificationBar = ServiceLocator.AcquireService<NotificationBar>();
        NotificationBarHost.Children.Add(_notificationBar);

        // Register panels with the workspace service
        workspaceService.SetPanels(utilityPanel, documentsPanel);

        // Add panels to the UI
        UtilityPanelHost.Children.Add(utilityPanel as UIElement);
        DocumentsPanelHost.Children.Add(documentsPanel as UIElement);

        // Enable the pointer-driven resource drag overlay on heads where the built-in drag-and-drop is
        // disabled. The panels register their drop targets with the coordinator as they load.
        if (_platformInfo.UsesPointerDrivenTabDrag)
        {
            _resourceDragCoordinator.Initialize(ResourceDragOverlay, LayoutRoot);
        }

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

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.IsUtilityPanelVisible):
                UpdatePanels();
                break;

            case nameof(ViewModel.UtilityPanelWidth):
                if (ViewModel.IsUtilityPanelVisible &&
                    ViewModel.UtilityPanelWidth > 0)
                {
                    UtilityPanelColumn.Width = new GridLength(ViewModel.UtilityPanelWidth);
                }
                break;
        }
    }

    private void UpdatePanels()
    {
        if (ViewModel.IsUtilityPanelVisible)
        {
            UtilityPanelSplitter.Visibility = Visibility.Visible;
            UtilityPanelHost.Visibility = Visibility.Visible;
            UtilityPanelColumn.MinWidth = MinUtilityPanelWidth;
            UtilityPanelColumn.Width = new GridLength(ViewModel.UtilityPanelWidth);
        }
        else
        {
            UtilityPanelSplitter.Visibility = Visibility.Collapsed;
            UtilityPanelHost.Visibility = Visibility.Collapsed;
            UtilityPanelColumn.MinWidth = 0;
            UtilityPanelColumn.Width = new GridLength(0);
        }
    }

    private void UtilityPanelSplitter_DragStarted(object? sender, EventArgs e)
    {
        _utilityPanelSplitterHelper?.OnDragStarted();
    }

    private void UtilityPanelSplitter_DragDelta(object? sender, double delta)
    {
        _utilityPanelSplitterHelper?.OnDragDelta(delta);
    }

    private void UtilityPanelSplitter_DoubleClicked(object? sender, EventArgs e)
    {
        _commandService.Execute<IResetSurfaceSizeCommand>(command =>
        {
            command.Surface = WorkspaceSurface.UtilityPanel;
        });
    }
}
