using Celbridge.Utilities;
using Celbridge.Resources.Views;
using Celbridge.Utilities.Services;
using Celbridge.Commands;

namespace Celbridge.Resources.Services;

public class ResourceService : IResourceService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICommandService _commandService;
    private readonly IProjectDataService _projectDataService;

    public IResourceRegistry ResourceRegistry { get; init; }

    private IResourceTreeView? _resourceTreeView;
    public IResourceTreeView ResourceTreeView 
    {
        get
        {
            return _resourceTreeView ?? throw new NullReferenceException("ResourceTreeView is null.");
        }
        set 
        { 
            _resourceTreeView = value; 
        }
    }

    public ResourceService(
        IServiceProvider serviceProvider,
        ICommandService commandService,
        IProjectDataService projectDataService,
        IUtilityService utilityService,
        IResourceRegistry resourceRegistry)
    {
        _serviceProvider = serviceProvider;
        _commandService = commandService;
        _projectDataService = projectDataService;

        // Delete the DeletedFiles folder to clean these archives up.
        // The DeletedFiles folder contain archived files and folders from previous delete commands.
        var tempFilename = utilityService.GetTemporaryFilePath(PathConstants.DeletedFilesFolder, string.Empty);
        var deletedFilesFolder = Path.GetDirectoryName(tempFilename)!;
        if (Directory.Exists(deletedFilesFolder))
        {
            Directory.Delete(deletedFilesFolder, true);
        }

        // Create the resource registry for the project.
        // The registry is populated later once the workspace UI is fully loaded.
        ResourceRegistry = resourceRegistry;
        ResourceRegistry.ProjectFolderPath = _projectDataService.LoadedProjectData!.ProjectFolderPath;
        
    }

    public object CreateResourcesPanel()
    {
        return _serviceProvider.GetRequiredService<ResourcesPanel>();
    }

    public async Task<Result> UpdateResourcesAsync()
    {
        var updateResult = ResourceRegistry.UpdateResourceRegistry();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to update resources. {updateResult.Error}");
        }

        var populateResult = await ResourceTreeView.PopulateTreeView(ResourceRegistry);
        if (populateResult.IsFailure)
        {
            return Result.Fail($"Failed to update resources. {populateResult.Error}");
        }

        return Result.Ok();
    }

    public async Task<Result> TransferResources(ResourceKey destFolderResource, IResourceTransfer transfer)
    {
        // Filter out any items where the destination resource already exists
        // Todo: If it's a single item, ask the user if they want to replace the existing resource
        transfer.TransferItems.RemoveAll(item =>
        {
            return ResourceRegistry.GetResource(item.DestResource).IsSuccess;
        });

        if (transfer.TransferItems.Count == 0)
        {
            // All resource items have been filtered out so nothing left to transfer
            return Result.Ok();
        }

        // If there are multiple items, assign the same undo group id to all commands.
        // This ensures that all commands are undone together in a single operation.
        var undoGroupId = transfer.TransferItems.Count > 1 ? EntityId.Create() : EntityId.InvalidId;

        foreach (var transferItem in transfer.TransferItems)
        {
            if (transferItem.SourceResource.IsEmpty)
            {
                // This resource is outside the project folder, add it using the AddResource command.
                _commandService.Execute<IAddResourceCommand>(command =>
                {
                    command.ResourceType = transferItem.ResourceType;
                    command.DestResource = transferItem.DestResource;
                    command.SourcePath = transferItem.SourcePath;
                    command.UndoGroupId = undoGroupId;
                });
            }
            else
            {
                // This resource is inside the project folder, copy/move it using the CopyResource command.
                _commandService.Execute<ICopyResourceCommand>(command =>
                {
                    command.SourceResource = transferItem.SourceResource;
                    command.DestResource = transferItem.DestResource;
                    command.TransferMode = transfer.TransferMode;
                    command.UndoGroupId = undoGroupId;
                });
            }
        }

        // Expand the destination folder so the user can see the newly transfered resources immediately.
        ResourceRegistry.SetFolderIsExpanded(destFolderResource, true);

        // Refresh the resource registry and tree view
        var updateResult = await UpdateResourcesAsync();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to update resources. {updateResult.Error}");
        }

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
            }

            _disposed = true;
        }
    }

    ~ResourceService()
    {
        Dispose(false);
    }
}
