using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Commands;

public class DeleteResourceDialogCommand : CommandBase, IDeleteResourceDialogCommand
{
    public override CommandFlags CommandFlags => CommandFlags.None;

    public List<ResourceKey> Resources { get; set; } = new();

    private readonly ILogger<DeleteResourceDialogCommand> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public DeleteResourceDialogCommand(
        ILogger<DeleteResourceDialogCommand> logger,
        IMessengerService messengerService,
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IDialogService dialogService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _dialogService = dialogService;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        return await ShowDeleteResourceDialogAsync();
    }

    private async Task<Result> ShowDeleteResourceDialogAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to show delete resource dialog because workspace is not loaded");
        }

        if (Resources.Count == 0)
        {
            return Result.Ok();
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var operations = _workspaceWrapper.WorkspaceService.ResourceService.Operations;
        var deleteString = _stringLocalizer.GetString("ResourceTree_Delete");

        // Pre-check the policy before confirming: a locked or path-frozen
        // resource cannot be deleted, so explain why rather than asking the user
        // to confirm a delete the operation layer would then refuse. The user
        // just triggered the delete, so the message names only the blocked file
        // (or the count, for a multi-selection) and the reason.
        var blockedResources = new List<(string Name, Result Failure)>();
        foreach (var resourceKey in Resources)
        {
            var canModifyResult = await operations.CanModifyResourceAsync(resourceKey);
            if (canModifyResult.IsFailure)
            {
                blockedResources.Add((resourceKey.ResourceName, canModifyResult));
            }
        }

        if (blockedResources.Count > 0)
        {
            var cannotDeleteTitle = _stringLocalizer.GetString("ResourceTree_CannotDelete");
            string messageText = blockedResources.Count == 1
                ? PolicyDenialFormatter.FormatReason(blockedResources[0].Failure, blockedResources[0].Name, _stringLocalizer)
                : _stringLocalizer.GetString("Policy_Locked_Multiple", blockedResources.Count);

            await _dialogService.ShowAlertDialogAsync(cannotDeleteTitle, messageText);

            return Result.Ok();
        }

        string confirmDeleteString;

        if (Resources.Count == 1)
        {
            // Single item - show specific name
            var getResult = resourceRegistry.GetResource(Resources[0]);
            if (getResult.IsFailure)
            {
                return Result.Fail($"Failed to resolve resource: '{Resources[0]}'")
                    .WithErrors(getResult);
            }
            var resource = getResult.Value;
            var resourceName = resource.Name;

            string confirmDeleteStringKey = resource switch
            {
                IFileResource => "ResourceTree_ConfirmDeleteFile",
                IFolderResource => "ResourceTree_ConfirmDeleteFolder",
                _ => throw new ArgumentException()
            };

            confirmDeleteString = _stringLocalizer.GetString(confirmDeleteStringKey, resourceName);
        }
        else
        {
            // Multiple items - show count
            confirmDeleteString = _stringLocalizer.GetString("ResourceTree_ConfirmDeleteMultiple", Resources.Count);
        }

        var showResult = await _dialogService.ShowConfirmationDialogAsync(deleteString, confirmDeleteString);
        if (showResult.IsSuccess)
        {
            var confirmed = showResult.Value;
            if (confirmed)
            {
                _logger.LogDebug($"Delete confirmed for {Resources.Count} resource(s), enqueueing DeleteResourceCommand.");

                // Execute a command to delete the resources (fire-and-forget to avoid deadlock)
                _commandService.Execute<IDeleteResourceCommand>(command =>
                {
                    command.Resources = Resources;
                });
            }
            else
            {
                _logger.LogDebug("Delete cancelled by user");
            }
        }
        else
        {
            _logger.LogWarning($"ShowConfirmationDialogAsync failed: {showResult.DiagnosticReport}");
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void DeleteResourceDialog(ResourceKey resource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IDeleteResourceDialogCommand>(command =>
        {
            command.Resources = [resource];
        });
    }

    public static void DeleteResourcesDialog(List<ResourceKey> resources)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IDeleteResourceDialogCommand>(command =>
        {
            command.Resources = resources;
        });
    }

}
