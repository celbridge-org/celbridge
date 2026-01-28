using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Celbridge.Logging;

namespace Celbridge.Resources.Commands;

public class DeleteResourceCommand : CommandBase, IDeleteResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.ForceUpdateResources;

    public ResourceKey Resource { get; set; }

    private readonly ILogger<DeleteResourceCommand> _logger;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public DeleteResourceCommand(
        ILogger<DeleteResourceCommand> logger,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var fileOpService = workspaceService.FileOperationService;

        var resourcePath = resourceRegistry.GetResourcePath(Resource);

        Result deleteResult;

        if (File.Exists(resourcePath))
        {
            deleteResult = await fileOpService.DeleteFileAsync(resourcePath);
            if (deleteResult.IsFailure)
            {
                _logger.LogError(deleteResult.Error);

                var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFile");
                var messageString = _stringLocalizer.GetString("ResourceTree_DeleteFileFailed", Resource);
                await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                return deleteResult;
            }
        }
        else if (Directory.Exists(resourcePath))
        {
            deleteResult = await fileOpService.DeleteFolderAsync(resourcePath);
            if (deleteResult.IsFailure)
            {
                _logger.LogError(deleteResult.Error);

                var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFolder");
                var messageString = _stringLocalizer.GetString("ResourceTree_DeleteFolderFailed", Resource);
                await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                return deleteResult;
            }
        }
        else
        {
            return Result.Fail($"Resource does not exist: {Resource}");
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void DeleteResource(ResourceKey resource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IDeleteResourceCommand>(command =>
        {
            command.Resource = resource;
        });
    }
}
