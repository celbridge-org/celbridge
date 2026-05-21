using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

// Per-resource outcome of CopySingleResourceAsync. ParentFolder is the
// expandable parent of the destination (null when nothing should expand).
// CopiedFolder is set when a folder was copied and the caller wants to track
// it for end-of-batch expansion. MoveDetail carries the FS-layer's structured
// result when the operation was a move; null for copy operations.
internal record CopyResourceOutcome(
    Result Result,
    ResourceKey? ParentFolder,
    ResourceKey? CopiedFolder,
    MoveResult? MoveDetail);

public class CopyResourceCommand : CommandBase, ICopyResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public List<ResourceKey> SourceResources { get; set; } = new();
    public ResourceKey DestResource { get; set; }
    public DataTransferMode TransferMode { get; set; }
    public bool ExpandCopiedFolder { get; set; }

    public CopyCommandResult ResultValue { get; private set; } = new(
        Array.Empty<ResourceKey>(),
        Array.Empty<SkippedReferencer>(),
        Array.Empty<ResourceKey>());

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

        // Hoist the workspace-scoped service lookups out of the per-resource
        // loop. Acquiring them inside ExecuteAsync (rather than via constructor
        // injection) honours the workspace-scoped DI rule — the workspace can
        // be swapped between executions, but it cannot change while a single
        // command runs, so caching for the duration of this call is safe.
        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var resourceOpService = workspaceService.ResourceService.OperationService;

        // Filter out resources whose parent folders are also selected.
        // This prevents duplicate operations when both a folder and its contents are selected.
        var filteredResources = FilterRedundantResources(SourceResources);

        // Begin batch for single undo operation
        resourceOpService.BeginBatch();

        List<ResourceKey> failedResources = new();
        List<ResourceKey> copiedFolders = new();
        List<ResourceKey> aggregatedUpdated = new();
        List<SkippedReferencer> aggregatedSkipped = new();
        ResourceKey? lastParentFolder = null;

        try
        {
            foreach (var sourceResource in filteredResources)
            {
                var outcome = await CopySingleResourceAsync(sourceResource, projectFolderPath, resourceRegistry, resourceOpService);

                if (outcome.Result.IsFailure)
                {
                    _logger.LogError(outcome.Result.DiagnosticReport);
                    failedResources.Add(sourceResource);
                }
                else if (outcome.ParentFolder.HasValue)
                {
                    lastParentFolder = outcome.ParentFolder;
                }

                if (outcome.CopiedFolder.HasValue)
                {
                    copiedFolders.Add(outcome.CopiedFolder.Value);
                }

                if (outcome.MoveDetail is not null)
                {
                    aggregatedUpdated.AddRange(outcome.MoveDetail.UpdatedReferencers);
                    aggregatedSkipped.AddRange(outcome.MoveDetail.SkippedReferencers);
                }
            }
        }
        finally
        {
            // Always commit batch - partial success is acceptable
            resourceOpService.CommitBatch();
        }

        ResultValue = new CopyCommandResult(
            aggregatedUpdated,
            aggregatedSkipped,
            failedResources);

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

        if (failedResources.Count > 0)
        {
            // ResourceOperationFailedMessage is a UI display channel and takes
            // a list of strings for the toast/banner. Convert from typed keys
            // to display names at this boundary; the structured CopyCommandResult
            // above keeps the typed ResourceKey list for programmatic callers.
            var failedDisplayNames = failedResources.Select(r => r.ResourceName).ToList();
            var failedList = string.Join(", ", failedDisplayNames);
            var operation = TransferMode == DataTransferMode.Copy ? "copy" : "move";
            _logger.LogWarning($"CopyResourceCommand completed with failures: {failedList}");

            // Notify the UI about the failure
            var operationType = TransferMode == DataTransferMode.Copy
                ? ResourceOperationType.Copy
                : ResourceOperationType.Move;
            var message = new ResourceOperationFailedMessage(operationType, failedDisplayNames);
            _messengerService.Send(message);

            return Result.Fail($"Failed to {operation}: {failedList}");
        }

        return Result.Ok();
    }

    private async Task<CopyResourceOutcome> CopySingleResourceAsync(
        ResourceKey sourceResource,
        string projectFolderPath,
        IResourceRegistry resourceRegistry,
        IResourceOperationService resourceOpService)
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
            return new CopyResourceOutcome(
                Result.Fail($"Resource does not exist: {sourcePath}"),
                ParentFolder: null,
                CopiedFolder: null,
                MoveDetail: null);
        }

        Result result;
        MoveResult? moveDetail = null;

        if (isFile)
        {
            if (TransferMode == DataTransferMode.Copy)
            {
                result = await resourceOpService.CopyFileAsync(sourcePath, destPath);
            }
            else
            {
                var moveResult = await resourceOpService.MoveFileAsync(sourcePath, destPath);
                result = moveResult;
                if (moveResult.IsSuccess)
                {
                    moveDetail = moveResult.Value;
                }
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
                var moveResult = await resourceOpService.MoveFolderAsync(sourcePath, destPath);
                result = moveResult;
                if (moveResult.IsSuccess)
                {
                    moveDetail = moveResult.Value;
                }
            }
        }

        ResourceKey? parentFolder = null;
        ResourceKey? copiedFolder = null;
        if (result.IsSuccess)
        {
            // Track the parent folder for expansion at the end
            var newParentFolder = resolvedDestResource.GetParent();
            if (!newParentFolder.IsEmpty)
            {
                parentFolder = newParentFolder;
            }
            if (isFolder)
            {
                copiedFolder = resolvedDestResource;
            }
        }

        return new CopyResourceOutcome(result, parentFolder, copiedFolder, moveDetail);
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
