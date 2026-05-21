using System.Text;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class DeleteResourceCommand : CommandBase, IDeleteResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public List<ResourceKey> Resources { get; set; } = new();

    public DeleteReferencePolicy ReferencePolicy { get; set; } = DeleteReferencePolicy.RequireConfirmation;

    public DeleteCommandResult ResultValue { get; private set; } = new(
        DeleteBatchOutcome.DeletedAll,
        Array.Empty<DeleteResourceResult>(),
        new Dictionary<ResourceKey, IReadOnlyList<ResourceKey>>());

    private readonly ILogger<DeleteResourceCommand> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IDialogService _dialogService;

    public DeleteResourceCommand(
        ILogger<DeleteResourceCommand> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        IDialogService dialogService)
    {
        _logger = logger;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _dialogService = dialogService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace is not loaded");
        }

        if (Resources.Count == 0)
        {
            ResultValue = new DeleteCommandResult(
                DeleteBatchOutcome.DeletedAll,
                Array.Empty<DeleteResourceResult>(),
                new Dictionary<ResourceKey, IReadOnlyList<ResourceKey>>());
            return Result.Ok();
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var resourceOpService = workspaceService.ResourceService.OperationService;
        var metaData = workspaceService.ResourceMetaData;

        await metaData.WaitUntilReadyAsync();

        // Phase A: aggregate referencers external to the batch. References
        // from one doomed resource to another are filtered out so an internal
        // dependency doesn't block the batch. Folder resources expand to every
        // descendant key the reference graph knows about, so a folder delete
        // surfaces incoming references to anything inside the folder, not just
        // references to the folder key itself (which are usually none).
        var batchSet = new HashSet<ResourceKey>(Resources);
        var folderResources = new List<ResourceKey>();
        foreach (var resource in Resources)
        {
            if (IsFolderResource(resourceRegistry, resource))
            {
                folderResources.Add(resource);
            }
        }
        bool IsInsideBatch(ResourceKey candidate)
        {
            if (batchSet.Contains(candidate))
            {
                return true;
            }
            foreach (var folder in folderResources)
            {
                if (candidate.IsDescendantOf(folder))
                {
                    return true;
                }
            }
            return false;
        }

        var externalReferencers = new Dictionary<ResourceKey, IReadOnlyList<ResourceKey>>();
        foreach (var resource in Resources)
        {
            var keysToCheck = new List<ResourceKey> { resource };
            if (folderResources.Contains(resource))
            {
                // The reference graph keys descendants by file, not by folder.
                // Walk every indexed target and pull in those that live under
                // this folder so we surface every incoming reference that the
                // recursive delete will leave dangling.
                foreach (var target in metaData.GetAllReferencedTargets())
                {
                    if (target.IsDescendantOf(resource))
                    {
                        keysToCheck.Add(target);
                    }
                }
            }

            // Emit one entry per specifically-referenced key (the folder key
            // itself or any descendant). Agents that act on a recursive folder
            // delete need to know which individual descendant file has external
            // references — collapsing to a single entry under the folder key
            // loses that granularity.
            foreach (var key in keysToCheck)
            {
                var perKeyReferencers = new List<ResourceKey>();
                foreach (var referencer in metaData.GetReferencers(key))
                {
                    if (!IsInsideBatch(referencer))
                    {
                        perKeyReferencers.Add(referencer);
                    }
                }
                if (perKeyReferencers.Count > 0)
                {
                    externalReferencers[key] = perKeyReferencers;
                }
            }
        }

        // Phase B: policy gate. No filesystem effects in this phase.
        if (externalReferencers.Count > 0)
        {
            switch (ReferencePolicy)
            {
                case DeleteReferencePolicy.FailIfReferenced:
                    ResultValue = new DeleteCommandResult(
                        DeleteBatchOutcome.BlockedByReferences,
                        Array.Empty<DeleteResourceResult>(),
                        externalReferencers);
                    return Result.Ok();

                case DeleteReferencePolicy.RequireConfirmation:
                    var dialogResult = await _dialogService.ShowConfirmationDialogAsync(
                        titleText: "Delete resources with existing references?",
                        messageText: BuildConfirmationMessage(Resources, externalReferencers));

                    if (dialogResult.IsFailure
                        || !dialogResult.Value)
                    {
                        ResultValue = new DeleteCommandResult(
                            DeleteBatchOutcome.CancelledByUser,
                            Array.Empty<DeleteResourceResult>(),
                            externalReferencers);
                        return Result.Ok();
                    }
                    break;

                case DeleteReferencePolicy.BreakReferences:
                    break;
            }
        }

        // Phase C: execute. Per-resource best-effort; mechanical failures do
        // not poison the batch. The soft-delete-to-trash path on
        // IResourceOperationService preserves undo and cascades the paired
        // sidecar alongside the parent.
        var resourceResults = new List<DeleteResourceResult>(Resources.Count);
        var failedItems = new List<string>();

        resourceOpService.BeginBatch();
        try
        {
            foreach (var resource in Resources)
            {
                var resolveResult = resourceRegistry.ResolveResourcePath(resource);
                if (resolveResult.IsFailure)
                {
                    _logger.LogWarning($"Cannot delete resource because path could not be resolved: '{resource}'");
                    resourceResults.Add(new DeleteResourceResult(
                        resource,
                        DeleteResourceOutcome.IOFailure,
                        SidecarOutcome.NotPresent,
                        FailureMessage: resolveResult.FirstErrorMessage));
                    failedItems.Add(resource.ResourceName);
                    continue;
                }
                var resourcePath = resolveResult.Value;

                bool sidecarPresent = SidecarExistsForResource(resourceRegistry, resource);

                Result deleteResult;
                if (File.Exists(resourcePath))
                {
                    deleteResult = await resourceOpService.DeleteFileAsync(resourcePath);
                }
                else if (Directory.Exists(resourcePath))
                {
                    deleteResult = await resourceOpService.DeleteFolderAsync(resourcePath);
                }
                else
                {
                    _logger.LogWarning($"Cannot delete resource because it does not exist: '{resource}'");
                    resourceResults.Add(new DeleteResourceResult(
                        resource,
                        DeleteResourceOutcome.NotFound,
                        SidecarOutcome.NotPresent,
                        FailureMessage: $"Resource does not exist: '{resource}'"));
                    failedItems.Add(resource.ResourceName);
                    continue;
                }

                if (deleteResult.IsFailure)
                {
                    var classification = ClassifyDeleteFailure(deleteResult);
                    _logger.LogError($"Failed to delete resource '{resource}': {deleteResult.DiagnosticReport}");
                    resourceResults.Add(new DeleteResourceResult(
                        resource,
                        classification.Outcome,
                        SidecarOutcome.NotPresent,
                        FailureMessage: classification.Message));
                    failedItems.Add(resource.ResourceName);
                    continue;
                }

                // The FS layer's DeleteFileOperation handles the parent and the
                // sidecar as one transactional unit — either both end up in the
                // trash or the whole delete fails. So on success, the sidecar
                // outcome is Cascaded if a sidecar existed, NotPresent if not.
                resourceResults.Add(new DeleteResourceResult(
                    resource,
                    DeleteResourceOutcome.Deleted,
                    sidecarPresent ? SidecarOutcome.Cascaded : SidecarOutcome.NotPresent,
                    FailureMessage: null));
            }
        }
        finally
        {
            resourceOpService.CommitBatch();
        }

        // Distinguish "every resource deleted cleanly" from "policy gate passed
        // but at least one resource failed mechanically". A human (or agent)
        // reading BatchOutcome should not have to inspect ResourceResults to learn
        // whether the batch was actually clean.
        var batchOutcome = failedItems.Count == 0
            ? DeleteBatchOutcome.DeletedAll
            : DeleteBatchOutcome.DeletedSome;

        ResultValue = new DeleteCommandResult(
            batchOutcome,
            resourceResults,
            externalReferencers);

        if (failedItems.Count > 0)
        {
            // Notify the UI about per-resource failures via the toast/banner
            // channel so the user gets a visible signal even if the calling
            // surface ignores ResultValue.
            var message = new ResourceOperationFailedMessage(ResourceOperationType.Delete, failedItems);
            _messengerService.Send(message);
        }

        // The command itself succeeded — the batch ran end-to-end (the policy
        // gate cleared, every resource was attempted). Per-resource failures
        // are surfaced through ResultValue.ResourceResults with typed outcomes
        // (NotFound, Locked, PermissionDenied, IOFailure) rather than collapsed
        // into a single Result.Fail string. Result.Fail at this layer is
        // reserved for cases where the batch couldn't run at all (workspace
        // not loaded, dialog dispatch failed, etc., handled earlier in this
        // method).
        return Result.Ok();
    }

    // Maps the exception attached to a failed delete result onto a typed
    // DeleteResourceOutcome reason. Mirrors the cascade's
    // ClassifyReferencerWriteFailure pattern so the agent gets the same
    // granularity for delete failures as for rename-cascade skips.
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

    private static bool IsFolderResource(IResourceRegistry registry, ResourceKey resource)
    {
        var resolveResult = registry.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return false;
        }
        return Directory.Exists(resolveResult.Value);
    }

    private static bool SidecarExistsForResource(IResourceRegistry registry, ResourceKey resource)
    {
        if (resource.IsEmpty)
        {
            return false;
        }

        var sidecarKey = new ResourceKey(resource.Root + ":" + resource.Path + Helpers.SidecarHelper.Extension);
        var resolveResult = registry.ResolveResourcePath(sidecarKey);
        if (resolveResult.IsFailure)
        {
            return false;
        }

        return File.Exists(resolveResult.Value);
    }

    private static string BuildConfirmationMessage(
        IReadOnlyList<ResourceKey> resources,
        IReadOnlyDictionary<ResourceKey, IReadOnlyList<ResourceKey>> externalReferencers)
    {
        var builder = new StringBuilder();
        if (resources.Count == 1)
        {
            builder.AppendLine($"The resource '{resources[0]}' is referenced by other project files:");
        }
        else
        {
            builder.AppendLine($"{externalReferencers.Count} of the {resources.Count} resources you are deleting are referenced by other project files:");
        }
        builder.AppendLine();

        foreach (var entry in externalReferencers)
        {
            builder.AppendLine($"  '{entry.Key}' is referenced by:");
            foreach (var referencer in entry.Value)
            {
                builder.AppendLine($"    - {referencer}");
            }
        }

        builder.AppendLine();
        builder.Append("Deleting them will leave the existing references broken. Continue?");
        return builder.ToString();
    }

    //
    // Static methods for scripting support.
    //

    public static void DeleteResource(ResourceKey resource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IDeleteResourceCommand>(command =>
        {
            command.Resources = new List<ResourceKey> { resource };
        });
    }

    public static void DeleteResources(List<ResourceKey> resources)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IDeleteResourceCommand>(command =>
        {
            command.Resources = resources;
        });
    }
}
