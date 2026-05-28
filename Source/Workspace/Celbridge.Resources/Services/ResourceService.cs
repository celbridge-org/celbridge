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

    public IResourceRegistry Registry { get; }
    public IRootHandlerRegistry RootHandlerRegistry { get; }
    public IResourceMonitor Monitor { get; }
    public IResourceTransferService TransferService { get; }
    public IResourceOperationService OperationService { get; }

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
        IResourceOperationService resourceOperationService)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _projectService = projectService;

        // RootHandlerRegistry and ResourceRegistry are constructed together so
        // they share the same root-handler instance
        var rootHandlerRegistry = new RootHandlerRegistry();
        RootHandlerRegistry = rootHandlerRegistry;

        Registry = new ResourceRegistry(
            registryLogger,
            messengerService,
            projectTreeBuilder,
            resourceClassifier,
            rootHandlerRegistry);

        Monitor = resourceMonitor;
        TransferService = resourceTransferService;
        OperationService = resourceOperationService;

        var projectFolderPath = _projectService.CurrentProject!.ProjectFolderPath;
        Registry.InitializeProjectRoot(projectFolderPath);

        // Build the new .celbridge/ hidden folder layout: temp/, logs/, trash/,
        // staging-fs/. These need to exist before downstream services start reading
        // or watching them.
        var celbridgeFolder = Path.Combine(projectFolderPath, ProjectConstants.CelbridgeFolder);
        var celbridgeTempFolder = Path.Combine(celbridgeFolder, ProjectConstants.CelbridgeTempFolder);
        var celbridgeLogsFolder = Path.Combine(celbridgeFolder, ProjectConstants.CelbridgeLogsFolder);
        var celbridgeTrashFolder = Path.Combine(celbridgeFolder, ProjectConstants.CelbridgeTrashFolder);
        var celbridgeStagingFsFolder = Path.Combine(celbridgeFolder, ProjectConstants.CelbridgeStagingFsFolder);

        Directory.CreateDirectory(celbridgeTempFolder);
        Directory.CreateDirectory(celbridgeLogsFolder);

        // Trash is cleared on every workspace load; undo history lives in memory only,
        // so previous-session trash content has no live handles.
        TryClearFolderContents(celbridgeTrashFolder);
        Directory.CreateDirectory(celbridgeTrashFolder);

        // staging-fs/ holds in-flight temp files for the resource file-system
        // chokepoint. Wipe orphans from a prior session crash before downstream
        // services start writing.
        TryClearFolderContents(celbridgeStagingFsFolder);
        Directory.CreateDirectory(celbridgeStagingFsFolder);

        // Legacy <project>/celbridge/.trash/ from before this redesign: discard.
        // The other legacy <project>/celbridge/.cache/, .logs/ folders are
        // left alone (no live data; they retire alongside the entity service).
        var legacyTrashFolder = Path.Combine(projectFolderPath, ProjectConstants.MetaDataFolder, ProjectConstants.TrashFolder);
        if (Directory.Exists(legacyTrashFolder))
        {
            try
            {
                Directory.Delete(legacyTrashFolder, true);
            }
            catch
            {
                // Best effort cleanup - ignore errors
            }
        }

        // Clean up the legacy temp folder from previous sessions. The atomic-write
        // staging area has moved to .celbridge/staging-fs/; any orphans here are
        // from before the chokepoint landed.
        var legacyTempFolder = Path.Combine(projectFolderPath, ProjectConstants.MetaDataFolder, ProjectConstants.TempFolder);
        if (Directory.Exists(legacyTempFolder))
        {
            try
            {
                Directory.Delete(legacyTempFolder, true);
            }
            catch
            {
                // Best effort cleanup - ignore errors
            }
        }

        // Register the temp: and logs: root handlers against the shared
        // PathValidator owned by the root handler registry so a single
        // InvalidatePathCache call covers project + temp + logs together.
        var sharedPathValidator = rootHandlerRegistry.PathValidator;
        rootHandlerRegistry.RegisterRootHandler(new TempRootHandler(celbridgeTempFolder, sharedPathValidator));
        rootHandlerRegistry.RegisterRootHandler(new LogsRootHandler(celbridgeLogsFolder, sharedPathValidator));

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

    private void OnResourceUpdateRequestedMessage(object recipient, RequestResourceRegistryUpdateMessage message)
    {
        var updateResult = UpdateResources();
        if (updateResult.IsFailure)
        {
            _logger.LogWarning(updateResult, "Failed to update resources after command execution");
        }
    }

    public void ScheduleResourceUpdate()
    {
        Monitor.ScheduleResourceUpdate();
    }

    public Result UpdateResources()
    {
        var updateResult = Registry.UpdateResourceRegistry();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to update resources. {updateResult.DiagnosticReport}");
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
                    ProjectConstants.CelbridgeTrashFolder);
                if (Directory.Exists(trashFolderPath))
                {
                    try
                    {
                        Directory.Delete(trashFolderPath, true);
                    }
                    catch
                    {
                        // Best effort cleanup - ignore errors
                    }
                }
            }

            _disposed = true;
        }
    }

    ~ResourceService()
    {
        Dispose(false);
    }

    // Removes every child item under the given folder while leaving the folder itself in place.
    // Used to clear .celbridge/trash/ on every workspace load without disturbing the folder layout.
    private static void TryClearFolderContents(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folderPath))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Best effort - ignore errors
            }
        }

        foreach (var subFolder in Directory.EnumerateDirectories(folderPath))
        {
            try
            {
                Directory.Delete(subFolder, true);
            }
            catch
            {
                // Best effort - ignore errors
            }
        }
    }
}
