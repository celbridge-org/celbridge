using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Explorer;
using Celbridge.Logging;
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
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;

    private IResourceFileSystem ResourceFileSystem => _workspaceWrapper.WorkspaceService.ResourceFileSystem;
    private IResourceOperationService ResourceOperationService => _workspaceWrapper.WorkspaceService.ResourceService.OperationService;
    private IResourceTransferService ResourceTransferService => _workspaceWrapper.WorkspaceService.ResourceService.TransferService;

    public CopyResourceCommand(
        ILogger<CopyResourceCommand> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _logger = logger;
        _messengerService = messengerService;
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

        // Filter out resources whose parent folders are also selected.
        // This prevents duplicate operations when both a folder and its contents are selected.
        var filteredResources = FilterRedundantResources(SourceResources);

        List<ResourceKey> failedResources = new();
        List<Result> failedOutcomes = new();
        List<ResourceKey> copiedFolders = new();
        List<ResourceKey> aggregatedUpdated = new();
        List<SkippedReferencer> aggregatedSkipped = new();
        ResourceKey? lastParentFolder = null;

        // Single undo unit for the whole batch; partial success is acceptable.
        using (var batch = ResourceOperationService.BeginBatch())
        {
            foreach (var sourceResource in filteredResources)
            {
                var outcome = await CopySingleResourceAsync(sourceResource);

                if (outcome.Result.IsFailure)
                {
                    _logger.LogError(outcome.Result.DiagnosticReport);
                    failedResources.Add(sourceResource);
                    failedOutcomes.Add(outcome.Result);
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
            _logger.LogWarning($"CopyResourceCommand completed with failures: {failedList}");

            // Notify the UI about the failure
            var operationType = TransferMode == DataTransferMode.Copy
                ? ResourceOperationType.Copy
                : ResourceOperationType.Move;
            var failedMessage = new ResourceOperationFailedMessage(operationType, failedDisplayNames);
            _messengerService.Send(failedMessage);

            // Propagate every per-resource failure into the bubble-up Result so
            // the agent sees the FS-layer's specific message (e.g.
            // "Destination already exists: '<key>'") via MessageChain. No outer
            // wrapper is added; the inner messages already identify which
            // resource(s) failed, and a generic summary string at the top would
            // duplicate that detail.
            var aggregated = Result.Fail();
            foreach (var failedOutcome in failedOutcomes)
            {
                aggregated.WithErrors(failedOutcome);
            }
            return aggregated;
        }

        return Result.Ok();
    }

    private async Task<CopyResourceOutcome> CopySingleResourceAsync(ResourceKey sourceResource)
    {
        // Resolve destination to handle folder drops
        var resolvedDestResource = ResourceTransferService.ResolveDestinationResource(sourceResource, DestResource);

        var infoResult = await ResourceFileSystem.GetInfoAsync(sourceResource);
        if (infoResult.IsFailure)
        {
            return new CopyResourceOutcome(
                Result.Fail($"Failed to probe source resource: '{sourceResource}'")
                    .WithErrors(infoResult),
                ParentFolder: null,
                CopiedFolder: null,
                MoveDetail: null);
        }
        var info = infoResult.Value;
        bool isFolder = info.Kind == StorageItemKind.Folder;

        if (info.Kind == StorageItemKind.NotFound)
        {
            return new CopyResourceOutcome(
                Result.Fail($"Resource does not exist: '{sourceResource}'"),
                ParentFolder: null,
                CopiedFolder: null,
                MoveDetail: null);
        }

        Result result;
        MoveResult? moveDetail = null;

        if (TransferMode == DataTransferMode.Copy)
        {
            var copyResult = await ResourceOperationService.CopyAsync(sourceResource, resolvedDestResource);
            result = copyResult;
        }
        else
        {
            var moveResult = await ResourceOperationService.MoveAsync(sourceResource, resolvedDestResource);
            result = moveResult;
            if (moveResult.IsSuccess)
            {
                moveDetail = moveResult.Value;
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
