using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Platform;
using Celbridge.UserInterface.DragDrop;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.ViewModels;

namespace Celbridge.WorkspaceUI.Views;

public sealed partial class WorkspacePage : Page, IWorkspaceView
{
    private readonly Logging.ILogger<WorkspacePage> _logger;
    private readonly IPlatformInfo _platformInfo;
    private readonly IResourceDragCoordinator _resourceDragCoordinator;

    public WorkspacePageViewModel ViewModel { get; }

    // Loaded can be raised more than once for one view, and the initialization below awaits, so a second
    // raise would otherwise start a duplicate workspace load.
    private bool _initialized = false;

    public CancellationTokenSource? LoadCancellation { get; set; }

    // The workspace notification toast, kept so its messenger subscriptions can be torn down with the
    // workspace.
    private WorkspaceToast? _workspaceToast;

    public WorkspacePage()
    {
        InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<WorkspacePageViewModel>();

        _logger = ServiceLocator.AcquireService<Logging.ILogger<WorkspacePage>>();
        _platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        _resourceDragCoordinator = ServiceLocator.AcquireService<IResourceDragCoordinator>();

        DataContext = ViewModel;

        Loaded += WorkspacePage_Loaded;
    }

    private async void WorkspacePage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        ViewModel.LoadProjectCancellationToken = LoadCancellation;

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

        // The toast overlays the surfaces rather than sitting in the layout, so it is always present and
        // costs nothing while no notification is showing.
        _workspaceToast = ServiceLocator.AcquireService<WorkspaceToast>();

        // Add panels to the UI
        WorkspacePanelHost.Children.Add(documentsPanel as UIElement);
        WorkspaceToastHost.Children.Add(_workspaceToast);

        // Enable the pointer-driven resource drag overlay on heads where the built-in drag-and-drop is
        // disabled. The panels register their drop targets with the coordinator as they load.
        if (_platformInfo.UsesPointerDrivenTabDrag)
        {
            _resourceDragCoordinator.Initialize(ResourceDragOverlay, LayoutRoot);
        }

        _ = ViewModel.LoadWorkspaceAsync();
    }

    public async Task TeardownAsync()
    {
        // Cleanup owned by this view: its message subscriptions. The workspace teardown (save editor
        // state, shut down panels, dispose the workspace) is orchestrated by the view-model.

        // Unbind the shared resource drag coordinator from this workspace. Safe when it was never
        // initialized (heads without the drag overlay).
        _resourceDragCoordinator.Reset();

        var messengerService = ServiceLocator.AcquireService<IMessengerService>();
        messengerService.UnregisterAll(this);

        // Tear down the toast's messenger subscriptions.
        _workspaceToast?.Cleanup();
        _workspaceToast = null;

        await ViewModel.OnWorkspacePageUnloadedAsync();

        Loaded -= WorkspacePage_Loaded;
    }
}
