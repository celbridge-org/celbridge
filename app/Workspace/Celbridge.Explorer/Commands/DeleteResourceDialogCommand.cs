using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Commands;

public class DeleteResourceDialogCommand : CommandBase, IDeleteResourceDialogCommand
{
    public override CommandFlags CommandFlags => CommandFlags.None;

    public ResourceKey Resource { get; set; }

    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public DeleteResourceDialogCommand(
        IMessengerService messengerService,
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IDialogService dialogService,
        IWorkspaceWrapper workspaceWrapper)
    {
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
            return Result.Fail($"Failed to show add resource dialog because workspace is not loaded");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(Resource);
        if (getResult.IsFailure)
        {
            return Result.Fail(getResult.Error);
        }
        var resource = getResult.Value;

        var resourceName = resource.Name;

        string confirmDeleteStringKey;
        switch (resource)
        {
            case IFileResource:
                confirmDeleteStringKey = "ResourceTree_ConfirmDeleteFile";
                break;
            case IFolderResource:
                confirmDeleteStringKey = "ResourceTree_ConfirmDeleteFolder";
                break;
            default:
                throw new ArgumentException();
        }

        var confirmDeleteString = _stringLocalizer.GetString(confirmDeleteStringKey, resourceName);
        var deleteString = _stringLocalizer.GetString("ResourceTree_Delete");

        var showResult = await _dialogService.ShowConfirmationDialogAsync(deleteString, confirmDeleteString);
        if (showResult.IsSuccess)
        {
            var confirmed = showResult.Value;
            if (confirmed)
            {
                // Execute a command to delete the resource
                _commandService.Execute<IDeleteResourceCommand>(command =>
                {
                    command.Resource = Resource;
                });
            }
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
            command.Resource = resource;
        });
    }
}
