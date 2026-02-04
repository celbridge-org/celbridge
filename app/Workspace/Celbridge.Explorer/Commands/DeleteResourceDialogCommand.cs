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
        var deleteString = _stringLocalizer.GetString("ResourceTree_Delete");

        string confirmDeleteString;

        if (Resources.Count == 1)
        {
            // Single item - show specific name
            var getResult = resourceRegistry.GetResource(Resources[0]);
            if (getResult.IsFailure)
            {
                return Result.Fail(getResult.Error);
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
            _logger.LogWarning($"ShowConfirmationDialogAsync failed: {showResult.Error}");
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
            command.Resources = new List<ResourceKey> { resource };
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
