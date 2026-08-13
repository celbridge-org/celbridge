using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Platform;
using Celbridge.UserInterface.DragDrop;
using Celbridge.UserInterface.Helpers;
using Celbridge.WorkspaceUI.ViewModels;

namespace Celbridge.WorkspaceUI.Views;

public sealed partial class WorkspacePage : Page
{
    private readonly Logging.ILogger<WorkspacePage> _logger;
    private readonly IPlatformInfo _platformInfo;
    private readonly IResourceDragCoordinator _resourceDragCoordinator;

    public WorkspacePageViewModel ViewModel { get; }

    private bool _initialized = false;

    // The project-notification banner strip, kept so its messenger subscriptions can be torn down on
    // page unload.
    private NotificationBar? _notificationBar;

    public WorkspacePage()
    {
        InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<WorkspacePageViewModel>();

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
        // The workspace panel composes every workspace surface, including the Utility Panel it hosts, and
        // registers both with the workspace service as it builds.
        var documentsPanel = ServiceLocator.AcquireService<IDocumentsPanel>();

        // The notification bar is not a layout surface, so it is always present. It collapses to zero
        // height when no banners are showing.
        _notificationBar = ServiceLocator.AcquireService<NotificationBar>();

        // Add panels to the UI
        WorkspacePanelHost.Children.Add(documentsPanel as UIElement);
        NotificationBarHost.Children.Add(_notificationBar);

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
        // Cleanup owned by this page: its message subscriptions. The workspace teardown (save editor
        // state, shut down panels, dispose the workspace) is orchestrated by the view-model.

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

}
