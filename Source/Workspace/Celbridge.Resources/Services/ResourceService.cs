using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Resources.Services.Roots;
using Celbridge.UserInterface;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Service for managing project resources including the resource registry, 
/// resource monitoring, and resource transfer operations.
/// </summary>
public class ResourceService : IResourceService, IDisposable
{
    private readonly ILogger<ResourceService> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly ILocalFileSystem _fileSystem;

    public IResourceRegistry Registry { get; }
    public IRootHandlerRegistry RootHandlers { get; }
    public IResourceMonitor Monitor { get; }
    public IResourceTransferService Transfers { get; }
    public IResourceOperationService Operations { get; }
    public IResourceFileSystem FileSystem { get; }
    public IResourcePolicy Policy { get; }
    public ITrashService Trash { get; }
    public IResourceScanner Scanner { get; }
    public ISidecarService Sidecars { get; }

    public ResourceService(
        ILogger<ResourceService> logger,
        ILogger<ResourceRegistry> registryLogger,
        ICommandService commandService,
        IMessengerService messengerService,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        IProjectTreeBuilder projectTreeBuilder,
        IResourceClassifier resourceClassifier,
        IResourceMonitor resourceMonitor,
        IResourceTransferService resourceTransferService,
        IResourceOperationService resourceOperationService,
        IResourceFileSystem resourceFileSystem,
        IResourcePolicy resourcePolicy,
        ITrashService trashService,
        IResourceScanner resourceScanner,
        ISidecarService sidecarService,
        ILocalFileSystem fileSystem)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _projectService = projectService;
        _fileSystem = fileSystem;

        // RootHandlerRegistry and ResourceRegistry are constructed together so
        // they share the same root-handler instance
        var rootHandlerRegistry = new RootHandlerRegistry();
        RootHandlers = rootHandlerRegistry;

        Registry = new ResourceRegistry(
            registryLogger,
            messengerService,
            projectTreeBuilder,
            resourceClassifier,
            rootHandlerRegistry,
            fileSystem);

        Monitor = resourceMonitor;
        Transfers = resourceTransferService;
        Operations = resourceOperationService;
        FileSystem = resourceFileSystem;
        Policy = resourcePolicy;
        Trash = trashService;
        Scanner = resourceScanner;
        Sidecars = sidecarService;

        var projectFolderPath = _projectService.CurrentProject!.ProjectFolderPath;
        Registry.InitializeProjectRoot(projectFolderPath);

        // Build the .celbridge/ hidden folder layout: temp/, logs/, trash/.
        // These need to exist before downstream services start reading or
        // watching them.
        var celbridgeFolder = Path.Combine(projectFolderPath, ProjectConstants.CelbridgeFolder);
        var celbridgeTempFolder = Path.Combine(celbridgeFolder, ProjectConstants.TempFolder);
        var celbridgeLogsFolder = Path.Combine(celbridgeFolder, ProjectConstants.LogsFolder);
        var celbridgeTrashFolder = Path.Combine(celbridgeFolder, ProjectConstants.TrashFolder);

        // temp:/ is wiped on every workspace load. The contract is that nothing
        // under temp: survives a reload; consumers needing persistence write
        // under project: instead.
        TryDeleteFolder(celbridgeTempFolder);
        SyncRunner.Run(() => _fileSystem.CreateFolderAsync(celbridgeTempFolder));
        SyncRunner.Run(() => _fileSystem.CreateFolderAsync(celbridgeLogsFolder));

        // Trash is cleared on every workspace load; undo history lives in memory only,
        // so previous-session trash content has no live handles.
        TryDeleteFolder(celbridgeTrashFolder);
        SyncRunner.Run(() => _fileSystem.CreateFolderAsync(celbridgeTrashFolder));

        // Discard the legacy <project>/celbridge/trash/ folder. The sibling
        // <project>/celbridge/cache/ has no live data and retires alongside
        // the entity service.
        var legacyTrashFolder = Path.Combine(projectFolderPath, LegacyConstants.MetaDataFolder, LegacyConstants.TrashFolder);
        TryDeleteFolder(legacyTrashFolder);

        // Discard the legacy <project>/celbridge/temp/ folder.
        var legacyTempFolder = Path.Combine(projectFolderPath, LegacyConstants.MetaDataFolder, LegacyConstants.TempFolder);
        TryDeleteFolder(legacyTempFolder);

        rootHandlerRegistry.RegisterRootHandler(new TempRootHandler(celbridgeTempFolder));
        rootHandlerRegistry.RegisterRootHandler(new LogsRootHandler(celbridgeLogsFolder));

        // Monitor.Initialize() is called from WorkspaceLoader after construction completes;
        // the monitor looks up its registry through IWorkspaceWrapper, which is only populated
        // once the WorkspaceService finishes constructing.

        _messengerService.Register<MainWindowActivatedMessage>(this, OnMainWindowActivatedMessage);
        _messengerService.Register<RequestResourceRegistryUpdateMessage>(this, OnResourceUpdateRequestedMessage);
    }

    private void OnMainWindowActivatedMessage(object recipient, MainWindowActivatedMessage message)
    {
#if !DEBUG
        // Refresh resources when the window gains focus to catch any external file system changes
        // Disabled in debug to avoid triggering an update every time we switch between the app and the debugger.
        _commandService.Execute<IUpdateResourcesCommand>();
#endif
    }

    // Fire-and-forget sink for the request message: the registry build is async,
    // so the handler awaits it and logs on failure rather than propagating.
    private async void OnResourceUpdateRequestedMessage(object recipient, RequestResourceRegistryUpdateMessage message)
    {
        var updateResult = await UpdateResourcesAsync();
        if (updateResult.IsFailure)
        {
            _logger.LogWarning(updateResult, "Failed to update resources after command execution");
        }
    }

    public void ScheduleResourceUpdate()
    {
        Monitor.ScheduleResourceUpdate();
    }

    public async Task<Result> UpdateResourcesAsync()
    {
        var updateResult = await Registry.UpdateResourceRegistryAsync();
        if (updateResult.IsFailure)
        {
            return Result.Fail("Failed to update resources")
                .WithErrors(updateResult);
        }

        _logger.LogDebug("Updated resources successfully.");

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
                // Dispose managed objects here
                _messengerService.UnregisterAll(this);

                // Shutdown the resource monitor immediately
                Monitor.Shutdown();

                // Clean up the trash folder on project close.
                // This ensures deleted files don't persist after the project is closed.
                var trashFolderPath = Path.Combine(
                    Registry.ProjectFolderPath,
                    ProjectConstants.CelbridgeFolder,
                    ProjectConstants.TrashFolder);
                TryDeleteFolder(trashFolderPath);
            }

            _disposed = true;
        }
    }

    ~ResourceService()
    {
        Dispose(false);
    }

    // Best-effort recursive folder removal. Failures are swallowed because
    // nothing downstream depends on the folder being gone — the workspace makes
    // another attempt next time.
    private void TryDeleteFolder(string folderPath)
    {
        var folderInfo = SyncRunner.Run(() => _fileSystem.GetInfoAsync(folderPath));
        if (folderInfo.IsFailure
            || folderInfo.Value.Kind != StorageItemKind.Folder)
        {
            return;
        }

        var deleteResult = SyncRunner.Run(() => _fileSystem.DeleteFolderAsync(folderPath, recursive: true));
        _ = deleteResult;
    }
}
