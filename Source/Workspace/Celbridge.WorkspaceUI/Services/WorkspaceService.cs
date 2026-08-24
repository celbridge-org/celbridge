using Celbridge.Console;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Search;

namespace Celbridge.WorkspaceUI.Services;

public class WorkspaceService : IWorkspaceService, IDisposable
{
    private readonly ILogger<WorkspaceService> _logger;
    private readonly IMessengerService _messengerService;

    public IWorkspaceSettingsService WorkspaceSettings { get; }
    public IBindableWorkspaceSettings BindableWorkspaceSettings { get; }
    public IPackageService PackageService { get; }
    public IResourceService ResourceService { get; }
    public IExplorerService ExplorerService { get; }
    public IDocumentsService DocumentsService { get; }
    public IUtilityService UtilityService { get; }
    public IConsoleService ConsoleService { get; }
    public ISearchService SearchService { get; }
    public IDataTransferService DataTransferService { get; }

    public WorkspacePanelId ActivePanel { get; private set; }

    public IUtilityPanel UtilityPanel { get; private set; } = null!;
    public IDocumentsPanel DocumentsPanel { get; private set; } = null!;

    private bool _workspaceStateIsDirty;

    public WorkspaceService(
        IServiceProvider serviceProvider,
        ILogger<WorkspaceService> logger,
        IMessengerService messengerService,
        IProjectService projectService)
    {
        _logger = logger;
        _messengerService = messengerService;

        // Create instances of the required sub-services

        WorkspaceSettings = serviceProvider.GetRequiredService<IWorkspaceSettingsService>();
        BindableWorkspaceSettings = serviceProvider.GetRequiredService<IBindableWorkspaceSettings>();
        PackageService = serviceProvider.GetRequiredService<IPackageService>();
        ResourceService = serviceProvider.GetRequiredService<IResourceService>();
        ExplorerService = serviceProvider.GetRequiredService<IExplorerService>();
        DocumentsService = serviceProvider.GetRequiredService<IDocumentsService>();
        UtilityService = serviceProvider.GetRequiredService<IUtilityService>();
        ConsoleService = serviceProvider.GetRequiredService<IConsoleService>();
        SearchService = serviceProvider.GetRequiredService<ISearchService>();
        DataTransferService = serviceProvider.GetRequiredService<IDataTransferService>();

        // Let the workspace settings service know where to find the workspace settings database.
        var project = projectService.CurrentProject;
        Guard.IsNotNull(project);
        var workspaceSettingsFolder = Path.Combine(
            project.ProjectDataFolderPath,
            ProjectConstants.SettingsFolder);
        Guard.IsNotNullOrEmpty(workspaceSettingsFolder);

        // The folder itself is created on demand by AcquireWorkspaceSettingsAsync.
        WorkspaceSettings.WorkspaceSettingsFolderPath = workspaceSettingsFolder;

        _messengerService.Register<WorkspaceStateDirtyMessage>(this, OnWorkspaceStateDirtyMessage);

        // The active panel is derived from the single focus arbiter rather than tracked separately.
        _messengerService.Register<PanelFocusChangedMessage>(this, OnPanelFocusChanged);
    }

    public void SetPanels(
        IUtilityPanel utilityPanel,
        IDocumentsPanel documentsPanel)
    {
        // Store panel references
        UtilityPanel = utilityPanel;
        DocumentsPanel = documentsPanel;
    }

    private void OnWorkspaceStateDirtyMessage(object recipient, WorkspaceStateDirtyMessage message)
    {
        _workspaceStateIsDirty = true;
    }

    private void OnPanelFocusChanged(object recipient, PanelFocusChangedMessage message)
    {
        // Focus on chrome (toolbars, dialogs) reports None. Keep the active panel on the last real panel so
        // panel-scoped undo still targets it after such an interaction.
        if (message.FocusedPanel != WorkspacePanelId.None)
        {
            ActivePanel = message.FocusedPanel;
        }
    }

    public async Task<Result> UpdateWorkspaceAsync(double deltaTime)
    {
        bool failed = false;

        if (_workspaceStateIsDirty)
        {
            _workspaceStateIsDirty = false;

            // Todo: Save the workspace state after a delay to avoid saving too frequently
            var saveWorkspaceResult = await SaveWorkspaceStateAsync();
            if (saveWorkspaceResult.IsFailure)
            {
                failed = true;
                _logger.LogError($"Failed to save workspace state. {saveWorkspaceResult.DiagnosticReport}");
            }
        }

        var saveDocumentsResult = await DocumentsService.SaveModifiedDocuments(deltaTime);
        if (saveDocumentsResult.IsFailure)
        {
            failed = true;
            _logger.LogError($"Failed to save modified documents. {saveDocumentsResult.DiagnosticReport}");
        }

        // Tick the utilities' save timers alongside the documents (their surfaces persist the same way).
        await UtilityService.SaveModifiedUtilities(deltaTime);

        // Flush any pending Workspace-scope setting writes (panel sizes, search
        // options, last new-file extension). These are set on the UI thread but
        // deferred, so the disk write happens here off the UI thread. FlushAsync
        // is a no-op when nothing has changed since the last tick.
        var workspaceSettingsStore = WorkspaceSettings.WorkspaceSettingsStore;
        if (workspaceSettingsStore is not null)
        {
            var flushResult = await workspaceSettingsStore.FlushAsync();
            if (flushResult.IsFailure)
            {
                failed = true;
                _logger.LogError($"Failed to flush workspace settings. {flushResult.DiagnosticReport}");
            }
        }

        // Todo: Clear save icon on the status bar if there are no pending saves

        if (failed)
        {
            return Result.Fail("Failed to update workspace");
        }

        return Result.Ok();
    }

    private async Task<Result> SaveWorkspaceStateAsync()
    {
        var folderStateService = ExplorerService.FolderStateService;
        await folderStateService.SaveAsync();

        return Result.Ok();
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Release the sub-services' resources on project close so editing multiple projects in a session does not leak.

                // Unregister message handlers
                _messengerService.UnregisterAll(this);

                // Dispose resource service first to stop file system monitoring
                (ResourceService as IDisposable)?.Dispose();
                (WorkspaceSettings as IDisposable)!.Dispose();
                (ConsoleService as IDisposable)!.Dispose();
                (DocumentsService as IDisposable)!.Dispose();
                (UtilityService as IDisposable)!.Dispose();
                (ExplorerService as IDisposable)!.Dispose();
                (SearchService as IDisposable)!.Dispose();
                (DataTransferService as IDisposable)!.Dispose();
            }

            _disposed = true;
        }
    }

    ~WorkspaceService()
    {
        Dispose(false);
    }
}
