using System.Diagnostics;
using Celbridge.Automation;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Resources.Services.Roots;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Service for managing project resources including the resource registry, 
/// resource monitoring, and resource transfer operations.
/// </summary>
public class ResourceService : IResourceService, IDisposable
{
    private readonly ILogger<ResourceService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly ILocalFileSystem _fileSystem;
    private readonly IAutomationService _automationService;
    private readonly ResourceUpdateScheduler _updateScheduler;

    // Resolved once at construction, so the unload clears exactly the folders the load created.
    private readonly string _tempFolderPath;
    private readonly string _trashFolderPath;

    public IResourceRegistry Registry { get; }
    public IRootHandlerRegistry RootHandlers { get; }
    public IResourceMonitor Monitor { get; }
    public IResourceTransferService Transfers { get; }
    public IResourceOperationService Operations { get; }
    public IResourceFileSystem FileSystem { get; }
    public IResourcePolicy Policy { get; }
    public ITrashService Trash { get; }
    public IResourceScanner Scanner { get; }
    public IResourceWalker Walker { get; }
    public ISidecarService Sidecars { get; }

    public ResourceService(
        ILogger<ResourceService> logger,
        ILogger<ResourceRegistry> registryLogger,
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
        IResourceWalker resourceWalker,
        ISidecarService sidecarService,
        ILocalFileSystem fileSystem,
        IAutomationService automationService,
        ResourceUpdateScheduler updateScheduler)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspaceLoaded);

        _logger = logger;
        _messengerService = messengerService;
        _projectService = projectService;
        _fileSystem = fileSystem;
        _automationService = automationService;
        _updateScheduler = updateScheduler;

        // RootHandlerRegistry and ResourceRegistry are constructed together so
        // they share the same root-handler instance
        var rootHandlerRegistry = new RootHandlerRegistry();
        RootHandlers = rootHandlerRegistry;

        Registry = new ResourceRegistry(
            registryLogger,
            messengerService,
            projectTreeBuilder,
            resourceClassifier,
            rootHandlerRegistry);

        Monitor = resourceMonitor;
        Transfers = resourceTransferService;
        Operations = resourceOperationService;
        FileSystem = resourceFileSystem;
        Policy = resourcePolicy;
        Trash = trashService;
        Scanner = resourceScanner;
        Walker = resourceWalker;
        Sidecars = sidecarService;

        var project = _projectService.CurrentProject!;

        // The backing folders are created here because downstream services start
        // reading and watching them as soon as the workspace loads.
        var projectDataFolder = project.ProjectDataFolderPath;
        var tempFolder = Path.Combine(projectDataFolder, ProjectConstants.TempFolder);
        var logsFolder = Path.Combine(projectDataFolder, ProjectConstants.LogsFolder);
        var utilsFolder = Path.Combine(projectDataFolder, ProjectConstants.UtilsFolder);
        var trashFolder = Path.Combine(projectDataFolder, ProjectConstants.TrashFolder);
        _tempFolderPath = tempFolder;
        _trashFolderPath = trashFolder;

        // temp:/ is wiped on every workspace load as well as on close, because a
        // crash skips the close. Nothing under temp: survives a reload, so
        // anything that must persist belongs under project:.
        TryDeleteFolder(tempFolder);
        SyncRunner.Run(() => _fileSystem.CreateFolderAsync(tempFolder));
        SyncRunner.Run(() => _fileSystem.CreateFolderAsync(logsFolder));

        // utils:/ is the persistent home for utility-document state, so it is
        // deliberately not wiped. The folder is created here so the watcher can
        // attach to it even before the first utility writes its state.
        SyncRunner.Run(() => _fileSystem.CreateFolderAsync(utilsFolder));

        // Trash is cleared on every workspace load; undo history lives in memory only,
        // so previous-session trash content has no live handles.
        TryDeleteFolder(trashFolder);
        SyncRunner.Run(() => _fileSystem.CreateFolderAsync(trashFolder));

        rootHandlerRegistry.RegisterRootHandler(new ProjectRootHandler(project.ProjectFolderPath));
        rootHandlerRegistry.RegisterRootHandler(new TempRootHandler(tempFolder));
        rootHandlerRegistry.RegisterRootHandler(new LogsRootHandler(logsFolder));
        rootHandlerRegistry.RegisterRootHandler(new UtilsRootHandler(utilsFolder));

        // Monitor.Initialize() is called from WorkspaceLoader after construction completes;
        // the monitor looks up its registry through IWorkspaceWrapper, which is only populated
        // once the WorkspaceService finishes constructing.

        _messengerService.Register<RequestResourceRegistryUpdateMessage>(this, OnResourceUpdateRequestedMessage);
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

    public Result StartMonitoring()
    {
        var initResult = Monitor.Initialize();
        if (initResult.IsFailure)
        {
            return initResult;
        }

        return _updateScheduler.Start(Monitor, Policy.SearchScope);
    }

    public void ScheduleResourceUpdate()
    {
        _updateScheduler.ScheduleUpdate();
    }

    public async Task<Result> UpdateResourcesAsync()
    {
        var stopwatch = Stopwatch.StartNew();

        var updateStartingMessage = new ResourceRegistryUpdateStartingMessage();
        _messengerService.Send(updateStartingMessage);

        var updateResult = await Registry.UpdateResourceRegistryAsync();
        if (updateResult.IsFailure)
        {
            return Result.Fail("Failed to update resources")
                .WithErrors(updateResult);
        }

        _logger.LogTrace("Updated resources successfully.");

        var elapsed = (long)stopwatch.Elapsed.TotalMilliseconds;
        _automationService.LogEvent(new ResourceRegistryUpdatedEvent(elapsed));

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
                _updateScheduler.Dispose();
                Monitor.Shutdown();

                // Delete the temp and trash folders on project close, so a closed project's data
                // folder keeps no scratch files or deleted files.
                TryDeleteFolder(_tempFolderPath);
                TryDeleteFolder(_trashFolderPath);
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
