using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Explorer.Views;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Utilities.Services;
using Celbridge.Utilities;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Explorer.Services;

public class ExplorerService : IExplorerService, IDisposable
{
    private const string PreviousSelectedResourceKey = "PreviousSelectedResource";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExplorerService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public IExplorerPanel? ExplorerPanel { get; private set; }

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

    public ResourceKey SelectedResource { get; private set; }

    private bool _isWorkspaceLoaded;

    public ExplorerService(
        IServiceProvider serviceProvider,
        ILogger<ExplorerService> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        IUtilityService utilityService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;

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
        ResourceRegistry = _serviceProvider.GetRequiredService<IResourceRegistry>();
        ResourceRegistry.ProjectFolderPath = _projectService.LoadedProject!.ProjectFolderPath;

        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoadedMessage);
        _messengerService.Register<SelectedResourceChangedMessage>(this, OnSelectedResourceChangedMessage);
    }

    private void OnWorkspaceLoadedMessage(object recipient, WorkspaceLoadedMessage message)
    {
        // Once set, this will remain true for the lifetime of the service
        _isWorkspaceLoaded = true;
    }

    private void OnSelectedResourceChangedMessage(object recipient, SelectedResourceChangedMessage message)
    {
        SelectedResource = message.Resource;

        if (_isWorkspaceLoaded)
        {
            // Ignore change events that happen while loading the workspace
            _ = StoreSelectedResource();            
        }
    }

    public IExplorerPanel CreateExplorerPanel()
    {
        ExplorerPanel = _serviceProvider.GetRequiredService<ExplorerPanel>();
        return ExplorerPanel;
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

    public Result<IResourceTransfer> CreateResourceTransfer(List<string> sourcePaths, ResourceKey destFolderResource, DataTransferMode transferMode)
    {
        var createItemsResult = CreateResourceTransferItems(sourcePaths, destFolderResource);
        if (createItemsResult.IsFailure)
        {
            var failure = Result<IResourceTransfer>.Fail($"Failed to create resource transfer items.");
            failure.MergeErrors(createItemsResult);
            return failure;
        }
        var transferItems = createItemsResult.Value;

        var resourceTransfer = new ResourceTransfer()
        {
            TransferMode = transferMode,
            TransferItems = transferItems
        };

        return Result<IResourceTransfer>.Ok(resourceTransfer);
    }

    private Result<List<ResourceTransferItem>> CreateResourceTransferItems(List<string> sourcePaths, ResourceKey destFolderResource)
    {
        try
        {
            List<ResourceTransferItem> transferItems = new();

            var destFolderPath = ResourceRegistry.GetResourcePath(destFolderResource);
            if (!Directory.Exists(destFolderPath))
            {
                return Result<List<ResourceTransferItem>>.Fail($"The path '{destFolderPath}' does not exist.");
            }

            foreach (var sourcePath in sourcePaths)
            {
                if (PathContainsSubPath(destFolderPath, sourcePath) &&
                    string.Compare(destFolderPath, sourcePath, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    // Ignore attempts to transfer a resource into a subfolder of itself.
                    // This check is case insensitive to err on the safe side for Windows file systems.
                    // Without this check, a transfer operation could generate thousands of nested folders!
                    // It is ok to "transfer" a resource to the same path however as this indicates a duplicate operation.
                    return Result<List<ResourceTransferItem>>.Fail($"Cannot transfer a resource into a subfolder of itself.");
                }

                ResourceType resourceType = ResourceType.Invalid;
                if (File.Exists(sourcePath))
                {
                    resourceType = ResourceType.File;
                }
                else if (Directory.Exists(sourcePath))
                {
                    resourceType = ResourceType.Folder;
                }
                else
                {
                    // Resource does not exist in the file system, ignore it.
                    continue;
                }

                var getKeyResult = ResourceRegistry.GetResourceKey(sourcePath);
                if (getKeyResult.IsSuccess)
                {
                    // This resource is inside the project folder so we should use the CopyResource command
                    // to copy/move it so that the resource meta data is preserved.
                    // This is indicated by having a non-empty source resource property.

                    var sourceResource = getKeyResult.Value;

                    // Sanity check that the generated sourceResource matches the original source path
                    var checkSourcePath = ResourceRegistry.GetResourcePath(sourceResource);
                    Guard.IsEqualTo(sourcePath, checkSourcePath);

                    var destResource = ResourceRegistry.ResolveDestinationResource(sourceResource, destFolderResource);

                    var item = new ResourceTransferItem(resourceType, sourcePath, sourceResource, destResource);
                    transferItems.Add(item);
                }
                else
                {
                    if (resourceType == ResourceType.File)
                    {
                        // This resource is outside the project folder, so we should add it to the project
                        // via the AddResource command, which will create new metadata for the resource.
                        // This is indicated by having an empty source resource property.
                        var sourceResource = new ResourceKey();
                        var filename = Path.GetFileName(sourcePath);
                        var destResource = destFolderResource.Combine(filename);

                        var item = new ResourceTransferItem(resourceType, sourcePath, sourceResource, destResource);
                        transferItems.Add(item);
                    }
                }
            }

            if (transferItems.Count == 0)
            {
                return Result<List<ResourceTransferItem>>.Fail($"Transfer item list is empty.");
            }

            return Result<List<ResourceTransferItem>>.Ok(transferItems);
        }
        catch (Exception ex)
        {
            return Result<List<ResourceTransferItem>>.Fail(ex, $"Failed to create resource transfer items.");
        }
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

    public Result SelectResource(ResourceKey resource)
    {
        Guard.IsNotNull(ExplorerPanel);

        return ExplorerPanel.SelectResource(resource);
    }

    public async Task StoreSelectedResource()
    {
        var workspaceData = _workspaceWrapper.WorkspaceService.WorkspaceDataService.LoadedWorkspaceData;
        Guard.IsNotNull(workspaceData);

        await workspaceData.SetPropertyAsync(PreviousSelectedResourceKey, SelectedResource.ToString());
    }

    public async Task RestorePanelState()
    {
        var workspaceData = _workspaceWrapper.WorkspaceService.WorkspaceDataService.LoadedWorkspaceData;
        Guard.IsNotNull(workspaceData);

        var resource = await workspaceData.GetPropertyAsync<string>(PreviousSelectedResourceKey);
        if (string.IsNullOrEmpty(resource))
        {
            return;
        }

        // Use ExecuteNow() to ensure the command is executed while the workspace is still loading.
        var selectResult = await _commandService.ExecuteNow<ISelectResourceCommand>(command =>
        {
            command.Resource = resource;
        });

        if (selectResult.IsFailure)
        {
            _logger.LogWarning(selectResult, $"Failed to select previously selected resource '{resource}'");
        }
    }

    private bool PathContainsSubPath(string path, string subPath)
    {
        string pathA = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string pathB = Path.GetFullPath(subPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return pathA.StartsWith(pathB, StringComparison.OrdinalIgnoreCase);
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
                _messengerService.Unregister<WorkspaceLoadedMessage>(this);
                _messengerService.Unregister<SelectedResourceChangedMessage>(this);
            }

            _disposed = true;
        }
    }

    ~ExplorerService()
    {
        Dispose(false);
    }
}
