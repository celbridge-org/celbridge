using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class DeleteResourceCommand : CommandBase, IDeleteResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public List<ResourceKey> Resources { get; set; } = new();

    public DeleteCommandResult ResultValue { get; private set; } = new(
        DeleteBatchOutcome.DeletedAll,
        Array.Empty<DeleteResourceResult>());

    private readonly ILogger<DeleteResourceCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ResourceOperationNotifier _operationNotifier;

    private IResourceFileSystem ResourceFileSystem => _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
    private IResourceOperationService ResourceOperationService => _workspaceWrapper.WorkspaceService.ResourceService.Operations;
    private ISidecarService SidecarService => _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;

    public DeleteResourceCommand(
        ILogger<DeleteResourceCommand> logger,
        IWorkspaceWrapper workspaceWrapper,
        ResourceOperationNotifier operationNotifier)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _operationNotifier = operationNotifier;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail("Workspace is not loaded");
        }

        if (Resources.Count == 0)
        {
            ResultValue = new DeleteCommandResult(
                DeleteBatchOutcome.DeletedAll,
                Array.Empty<DeleteResourceResult>());
            return Result.Ok();
        }

        // Each resource is deleted on a best-effort basis. A mechanical failure
        // leaves the rest of the batch to run. The soft delete to the trash
        // keeps undo working and moves the paired sidecar with its parent.
        var resourceResults = new List<DeleteResourceResult>(Resources.Count);
        var failedResources = new List<FailedResource>();

        using (var batch = ResourceOperationService.BeginBatch())
        {
            foreach (var resource in Resources)
            {
                // Probe the sidecar up front so we can report whether the delete
                // cascaded one. After the delete runs the sidecar is gone (or
                // never existed), so the only honest moment to ask is now.
                bool sidecarPresent = false;
                var sidecarKeyResult = SidecarService.GetSidecarKey(resource);
                if (sidecarKeyResult.IsSuccess)
                {
                    var sidecarInfoResult = await ResourceFileSystem.GetInfoAsync(sidecarKeyResult.Value);
                    sidecarPresent = sidecarInfoResult.IsSuccess
                        && sidecarInfoResult.Value.Kind == StorageItemKind.File;
                }

                var infoResult = await ResourceFileSystem.GetInfoAsync(resource);
                if (infoResult.IsFailure)
                {
                    _logger.LogWarning($"Cannot delete resource because info probe failed: '{resource}'");
                    resourceResults.Add(new DeleteResourceResult(
                        resource,
                        DeleteResourceOutcome.IOFailure,
                        SidecarOutcome.NotPresent,
                        Undoable: false,
                        FailureMessage: infoResult.MessageChain));
                    failedResources.Add(new FailedResource(resource, infoResult.MessageChain));
                    continue;
                }
                var info = infoResult.Value;

                if (info.Kind == StorageItemKind.NotFound)
                {
                    var notFoundMessage = $"Resource does not exist: '{resource}'";
                    _logger.LogWarning($"Cannot delete resource because it does not exist: '{resource}'");
                    resourceResults.Add(new DeleteResourceResult(
                        resource,
                        DeleteResourceOutcome.NotFound,
                        SidecarOutcome.NotPresent,
                        Undoable: false,
                        FailureMessage: notFoundMessage));
                    failedResources.Add(new FailedResource(resource, notFoundMessage));
                    continue;
                }

                var deleteResult = await ResourceOperationService.DeleteAsync(resource);

                if (deleteResult.IsFailure)
                {
                    var classification = ClassifyDeleteFailure(deleteResult);
                    _logger.LogError($"Failed to delete resource '{resource}': {deleteResult.DiagnosticReport}");
                    resourceResults.Add(new DeleteResourceResult(
                        resource,
                        classification.Outcome,
                        SidecarOutcome.NotPresent,
                        Undoable: false,
                        FailureMessage: classification.Message));
                    failedResources.Add(new FailedResource(resource, classification.Message));
                    continue;
                }
                var undoable = deleteResult.Value;

                // The FS layer's DeleteFileOperation handles the parent and the
                // sidecar as one transactional unit — either both end up in the
                // trash or the whole delete fails. So on success, the sidecar
                // outcome is Cascaded if a sidecar existed, NotPresent if not.
                resourceResults.Add(new DeleteResourceResult(
                    resource,
                    DeleteResourceOutcome.Deleted,
                    sidecarPresent ? SidecarOutcome.Cascaded : SidecarOutcome.NotPresent,
                    Undoable: undoable,
                    FailureMessage: null));
            }
        }

        // BatchOutcome says on its own whether every resource was deleted.
        var batchOutcome = failedResources.Count == 0
            ? DeleteBatchOutcome.DeletedAll
            : DeleteBatchOutcome.DeletedSome;

        ResultValue = new DeleteCommandResult(
            batchOutcome,
            resourceResults);

        // Notify the user about per-resource failures so they get a visible signal even if the
        // calling surface ignores ResultValue.
        await _operationNotifier.NotifyFailuresAsync(ResourceOperationType.Delete, failedResources);

        // Return Ok even when resources failed. ResultValue.ResourceResults
        // gives each resource's outcome. The command fails only when the
        // workspace is not loaded.
        return Result.Ok();
    }

    // Maps the exception attached to a failed delete result onto a typed
    // DeleteResourceOutcome reason.
    //
    // The DOS read-only attribute is cleared by DeleteFileOperation before the
    // move into the trash folder, so any UnauthorizedAccessException reaching
    // this point is a genuine ACL / POSIX denial rather than a clearable flag.
    private static (DeleteResourceOutcome Outcome, string Message) ClassifyDeleteFailure(Result deleteResult)
    {
        var exception = deleteResult.FirstException;

        if (exception is FileNotFoundException
            || exception is DirectoryNotFoundException)
        {
            return (DeleteResourceOutcome.NotFound, "resource does not exist on disk");
        }

        if (exception is UnauthorizedAccessException)
        {
            return (DeleteResourceOutcome.PermissionDenied, "permission denied (no write access)");
        }

        if (exception is IOException)
        {
            // The most common cause of an IOException during delete is a
            // sharing violation (file held open by an editor, antivirus,
            // backup tool). The hedged message points the user at where to
            // look without overcommitting to a cause we can't always confirm.
            return (DeleteResourceOutcome.Locked, "in use by another process (file may be locked by an editor or antivirus)");
        }

        return (DeleteResourceOutcome.IOFailure, deleteResult.FirstErrorMessage);
    }
}
