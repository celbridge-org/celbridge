using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Projects;
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
    public IResourceMonitor Monitor { get; }
    public IResourceTransferService TransferService { get; }
    public IResourceOperationService OperationService { get; }

    public ResourceService(
        ILogger<ResourceService> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        IResourceRegistry resourceRegistry,
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

        Registry = resourceRegistry;
        Monitor = resourceMonitor;
        TransferService = resourceTransferService;
        OperationService = resourceOperationService;

        // Set the project folder path on the registry
        Registry.ProjectFolderPath = _projectService.CurrentProject!.ProjectFolderPath;

        // Clean up the trash folder from previous sessions.
        // The trash folder contains soft-deleted files and folders from previous delete operations.
        var projectFolderPath = _projectService.CurrentProject!.ProjectFolderPath;
        var trashFolderPath = Path.Combine(projectFolderPath, ProjectConstants.MetaDataFolder, ProjectConstants.TrashFolder);
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

        // Initialize the resource monitor to start watching for file system changes
        var initResult = Monitor.Initialize();
        if (initResult.IsFailure)
        {
            _logger.LogWarning(initResult, "Failed to initialize resource monitor");
        }

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
        if (message.ForceImmediate)
        {
            var updateResult = UpdateResources();
            if (updateResult.IsFailure)
            {
                _logger.LogWarning(updateResult, "Failed to update resources after command execution");
            }
        }
        else
        {
            ScheduleResourceUpdate();
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
            return Result.Fail($"Failed to update resources. {updateResult.Error}");
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
                var trashFolderPath = Path.Combine(Registry.ProjectFolderPath, ProjectConstants.MetaDataFolder, ProjectConstants.TrashFolder);
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
}
