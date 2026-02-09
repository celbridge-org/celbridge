using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class CopyResourceCommand : CommandBase, ICopyResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.RequestUpdateResources;

    public List<ResourceKey> SourceResources { get; set; } = new();
    public ResourceKey DestResource { get; set; }
    public DataTransferMode TransferMode { get; set; }
    public bool ExpandCopiedFolder { get; set; }

    private readonly ILogger<CopyResourceCommand> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;

    public CopyResourceCommand(
        ILogger<CopyResourceCommand> logger,
        IMessengerService messengerService,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _logger = logger;
        _messengerService = messengerService;
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
        _commandService = commandService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Workspace is not loaded");
        }

        if (SourceResources.Count == 0)
        {
            return Result.Ok();
        }

        var project = _projectService.CurrentProject;
        Guard.IsNotNull(project);

        var projectFolderPath = project.ProjectFolderPath;
        if (string.IsNullOrEmpty(projectFolderPath))
        {
            return Result.Fail("Project folder path is empty.");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var resourceOpService = workspaceService.ResourceService.OperationService;

        // Filter out resources whose parent folders are also selected.
        // This prevents duplicate operations when both a folder and its contents are selected.
        var filteredResources = FilterRedundantResources(SourceResources);

        // Begin batch for single undo operation
        resourceOpService.BeginBatch();

        List<string> failedItems = new();
        List<ResourceKey> copiedFolders = new();
        ResourceKey? lastParentFolder = null;

        try
        {
            foreach (var sourceResource in filteredResources)
            {
                var (result, parentFolder) = await CopySingleResourceAsync(
                    sourceResource,
                    projectFolderPath,
                    resourceRegistry,
                    resourceOpService,
                    copiedFolders);

                if (result.IsFailure)
                {
                    _logger.LogError(result.Error);
                    failedItems.Add(sourceResource.ResourceName);
                }
                else if (parentFolder.HasValue)
                {
                    lastParentFolder = parentFolder;
                }
            }
        }
        finally
        {
            // Always commit batch - partial success is acceptable
            resourceOpService.CommitBatch();
        }

        // Expand destination folder once at end (not per-item)
        if (lastParentFolder.HasValue && !lastParentFolder.Value.IsEmpty)
        {
            _commandService.Execute<IExpandFolderCommand>(command =>
            {
                command.FolderResource = lastParentFolder.Value;
                command.Expanded = true;
            });
        }

        // Expand copied folders if requested
        if (ExpandCopiedFolder)
        {
            foreach (var folder in copiedFolders)
            {
                _commandService.Execute<IExpandFolderCommand>(command =>
                {
                    command.FolderResource = folder;
                    command.Expanded = true;
                });
            }
        }

        if (failedItems.Count > 0)
        {
            var failedList = string.Join(", ", failedItems);
            var operation = TransferMode == DataTransferMode.Copy ? "copy" : "move";
            _logger.LogWarning($"CopyResourceCommand completed with failures: {failedList}");

            // Notify the UI about the failure
            var operationType = TransferMode == DataTransferMode.Copy
                ? ResourceOperationType.Copy
                : ResourceOperationType.Move;
            var message = new ResourceOperationFailedMessage(operationType, failedItems);
            _messengerService.Send(message);

            return Result.Fail($"Failed to {operation}: {failedList}");
        }

        return Result.Ok();
    }

    private async Task<(Result result, ResourceKey? parentFolder)> CopySingleResourceAsync(
        ResourceKey sourceResource,
        string projectFolderPath,
        IResourceRegistry resourceRegistry,
        IResourceOperationService resourceOpService,
        List<ResourceKey> copiedFolders)
    {
        // Resolve destination to handle folder drops
        var resolvedDestResource = resourceRegistry.ResolveDestinationResource(sourceResource, DestResource);

        // Convert resource keys to paths
        var sourcePath = Path.GetFullPath(Path.Combine(projectFolderPath, sourceResource));
        var destPath = Path.GetFullPath(Path.Combine(projectFolderPath, resolvedDestResource));

        // Determine resource type
        bool isFile = File.Exists(sourcePath);
        bool isFolder = Directory.Exists(sourcePath);

        if (!isFile && !isFolder)
        {
            return (Result.Fail($"Resource does not exist: {sourcePath}"), null);
        }

        Result result;

        if (isFile)
        {
            if (TransferMode == DataTransferMode.Copy)
            {
                result = await resourceOpService.CopyFileAsync(sourcePath, destPath);
            }
            else
            {
                result = await resourceOpService.MoveFileAsync(sourcePath, destPath);
            }
        }
        else
        {
            if (TransferMode == DataTransferMode.Copy)
            {
                result = await resourceOpService.CopyFolderAsync(sourcePath, destPath);
            }
            else
            {
                result = await resourceOpService.MoveFolderAsync(sourcePath, destPath);
            }

            if (result.IsSuccess)
            {
                copiedFolders.Add(resolvedDestResource);
            }
        }

        ResourceKey? parentFolder = null;
        if (result.IsSuccess)
        {
            // Track the parent folder for expansion at the end
            var newParentFolder = resolvedDestResource.GetParent();
            if (!newParentFolder.IsEmpty)
            {
                parentFolder = newParentFolder;
            }
        }

        return (result, parentFolder);
    }

    /// <summary>
    /// Filters out resources that are descendants of other selected resources.
    /// This prevents duplicate operations when both a folder and its contents are selected.
    /// </summary>
    private static List<ResourceKey> FilterRedundantResources(List<ResourceKey> resources)
    {
        if (resources.Count <= 1)
        {
            return resources;
        }

        var result = new List<ResourceKey>();

        foreach (var resource in resources)
        {
            // Check if any other selected resource is an ancestor of this one
            var isRedundant = resources.Any(other =>
                !other.Equals(resource) && resource.IsDescendantOf(other));

            if (!isRedundant)
            {
                result.Add(resource);
            }
        }

        return result;
    }

    //
    // Static methods for scripting support.
    //

    public static void CopyResource(ResourceKey sourceResource, ResourceKey destResource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICopyResourceCommand>(command =>
        {
            command.SourceResources = [sourceResource];
            command.DestResource = destResource;
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    public static void CopyResources(List<ResourceKey> sourceResources, ResourceKey destResource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICopyResourceCommand>(command =>
        {
            command.SourceResources = sourceResources;
            command.DestResource = destResource;
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    public static void MoveResource(ResourceKey sourceResource, ResourceKey destResource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICopyResourceCommand>(command =>
        {
            command.SourceResources = [sourceResource];
            command.DestResource = destResource;
            command.TransferMode = DataTransferMode.Move;
        });
    }

    public static void MoveResources(List<ResourceKey> sourceResources, ResourceKey destResource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICopyResourceCommand>(command =>
        {
            command.SourceResources = sourceResources;
            command.DestResource = destResource;
            command.TransferMode = DataTransferMode.Move;
        });
    }
}
