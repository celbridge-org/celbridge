using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Platform;
using Celbridge.UserInterface.DragDrop;
using Celbridge.UserInterface.Helpers;
using Celbridge.WorkspaceUI.ViewModels;
using Windows.Foundation;

namespace Celbridge.WorkspaceUI.Views;

public sealed partial class WorkspacePage : Page
{
    private readonly ICommandService _commandService;
    private readonly Logging.ILogger<WorkspacePage> _logger;
    private readonly IPlatformInfo _platformInfo;
    private readonly IResourceDragCoordinator _resourceDragCoordinator;

    public WorkspacePageViewModel ViewModel { get; }

    private bool _initialized = false;

    private SplitterHelper? _utilityPanelSplitterHelper;

    // The panels the workspace lays out. Each reports its own minimum size, which the workspace composes
    // its own from.
    private IUtilityPanel? _utilityPanel;
    private IDocumentsPanel? _documentsPanel;

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

        // The stored width arrives with the workspace settings, so the Utility Panel opens at its default
        // until then.
        UtilityPanelColumn.Width = new GridLength(WorkspaceConstants.UtilityPanelWidth);

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

        DocumentsPanelGutterRow.Height = new GridLength(GutterSize);

        UtilityPanelHost.SizeChanged += (s, e) => ViewModel.UtilityPanelWidth = (float)e.NewSize.Width;

        // The Utility Panel takes whatever the documents panel beside it does not need.
        _utilityPanelSplitterHelper = new SplitterHelper(LayoutRoot, GridResizeMode.Columns, 0,
            minSizeFunc: () => UtilityPanelMinimumWidth,
            maxSizeFunc: () => WorkspaceMinimumSize.SpaceBeside(LayoutRoot.ActualWidth, DocumentsPanelMinimumSize.Width, GutterSize));

        UtilityPanelSplitter.DragStarted += UtilityPanelSplitter_DragStarted;
        UtilityPanelSplitter.DragDelta += UtilityPanelSplitter_DragDelta;
        UtilityPanelSplitter.DoubleClicked += UtilityPanelSplitter_DoubleClicked;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Populate the workspace panels.
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        var workspaceService = workspaceWrapper.WorkspaceService;
        Guard.IsNotNull(workspaceService);

        // Create panels via DI
        _utilityPanel = ServiceLocator.AcquireService<IUtilityPanel>();
        _documentsPanel = ServiceLocator.AcquireService<IDocumentsPanel>();

        // The notification bar is not a layout surface, so it is always present. It collapses to zero
        // height when no banners are showing.
        _notificationBar = ServiceLocator.AcquireService<NotificationBar>();
        NotificationBarHost.Children.Add(_notificationBar);

        // Register panels with the workspace service
        workspaceService.SetPanels(_utilityPanel, _documentsPanel);

        // Add panels to the UI
        UtilityPanelHost.Children.Add(_utilityPanel as UIElement);
        DocumentsPanelHost.Children.Add(_documentsPanel as UIElement);

        // Runs once the panels exist, because the sizes it applies are composed from the minimums they report.
        UpdatePanels();

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
            UtilityPanelColumn.MinWidth = UtilityPanelMinimumWidth;
            UtilityPanelHost.MinWidth = UtilityPanelMinimumWidth;
            UtilityPanelColumn.Width = new GridLength(ViewModel.UtilityPanelWidth);
        }
        else
        {
            UtilityPanelSplitter.Visibility = Visibility.Collapsed;
            UtilityPanelHost.Visibility = Visibility.Collapsed;
            UtilityPanelColumn.MinWidth = 0;
            UtilityPanelHost.MinWidth = 0;
            UtilityPanelColumn.Width = new GridLength(0);
        }
    }

    /// <summary>
    /// The smallest size the workspace can be laid out at: the Utility Panel beside the documents panel, and
    /// the channel above the documents panel.
    /// </summary>
    public Size MinimumSize
    {
        get
        {
            var documentsPanelMinimumSize = DocumentsPanelMinimumSize;

            double width = WorkspaceMinimumSize.ComposeAdjacent(
                UtilityPanelMinimumWidth,
                documentsPanelMinimumSize.Width,
                GutterSize);

            return new Size(width, documentsPanelMinimumSize.Height + GutterSize);
        }
    }

    // Zero while the Utility Panel is hidden or has not been created yet, so it contributes nothing to the
    // workspace minimum and its channel goes with it.
    private double UtilityPanelMinimumWidth
    {
        get
        {
            if (_utilityPanel is null ||
                !ViewModel.IsUtilityPanelVisible)
            {
                return 0;
            }

            return _utilityPanel.MinimumWidth;
        }
    }

    // Zero until the documents panel has been created.
    private Size DocumentsPanelMinimumSize
    {
        get
        {
            if (_documentsPanel is null)
            {
                return new Size(0, 0);
            }

            return new Size(_documentsPanel.MinimumWidth, _documentsPanel.MinimumHeight);
        }
    }

    // The channel between two panels. The splitter in it takes this size, which is what holds the gap open.
    private static double GutterSize => (double)Application.Current.Resources["GutterSize"];

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
