using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Resources;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Projects.Commands;

public class DeleteResourceDialogCommand : CommandBase, IDeleteResourceDialogCommand
{
    public override string UndoStackName => UndoStackNames.None;
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey Resource { get; set; }

    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IDialogService _dialogService;

    public DeleteResourceDialogCommand(
        IMessengerService messengerService,
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IDialogService dialogService)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _dialogService = dialogService;
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

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;

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
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IDeleteResourceDialogCommand>(command =>
        {
            command.Resource = resource;
        });
    }
}
