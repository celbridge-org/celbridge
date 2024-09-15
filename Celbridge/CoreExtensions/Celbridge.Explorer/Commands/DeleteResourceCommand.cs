using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Utilities;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Celbridge.Explorer.Services;

namespace Celbridge.Explorer.Commands
{
    public class DeleteResourceCommand : CommandBase, IDeleteResourceCommand
    {
        public override string UndoStackName => UndoStackNames.Project;
        public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

        public ResourceKey Resource { get; set; }

        private ResourceArchiver _archiver;

        private readonly IWorkspaceWrapper _workspaceWrapper;
        private readonly IUtilityService _utilityService;
        private readonly IDialogService _dialogService;
        private readonly IStringLocalizer _stringLocalizer;

        public DeleteResourceCommand(
            IServiceProvider serviceProvider,
            IWorkspaceWrapper workspaceWrapper,
            IUtilityService utilityService,
            IDialogService dialogService,
            IStringLocalizer stringLocalizer)
        {
            _workspaceWrapper = workspaceWrapper;
            _utilityService = utilityService;
            _dialogService = dialogService;
            _stringLocalizer = stringLocalizer;

            _archiver = serviceProvider.GetRequiredService<ResourceArchiver>();
        }

        public override async Task<Result> ExecuteAsync()
        {
            var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

            var getResourceResult = resourceRegistry.GetResource(Resource);
            if (getResourceResult.IsFailure)
            {
                return Result.Fail($"Failed to get resource: {Resource}");
            }

            var resource = getResourceResult.Value;

            if (resource is IFileResource)
            {
                var deleteResult = await _archiver.DeleteResourceAsync(Resource);
                if (deleteResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFile");
                    var messageString = _stringLocalizer.GetString("ResourceTree_DeleteFileFailed", Resource);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    return deleteResult;
                }
            }
            else if (resource is IFolderResource)
            {
                var deleteResult = await _archiver.DeleteResourceAsync(Resource);
                if (deleteResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFolder");
                    var messageString = _stringLocalizer.GetString("ResourceTree_DeleteFolderFailed", Resource);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    return deleteResult;
                }
            }
            else
            {
                return Result.Fail($"Unknown resource type for key: {Resource}");
            }

            return Result.Ok();
        }

        public override async Task<Result> UndoAsync()
        {
            if (_archiver.DeletedResourceType == ResourceType.File)
            {
                var undoResult = await _archiver.UndoDeleteResourceAsync();
                if (undoResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFile");
                    var messageString = _stringLocalizer.GetString("ResourceTree_UndoDeleteFileFailed", Resource);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    return undoResult;
                }
            }
            else if (_archiver.DeletedResourceType == ResourceType.Folder)
            {
                var undoResult = await _archiver.UndoDeleteResourceAsync();
                if (undoResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFolder");
                    var messageString = _stringLocalizer.GetString("ResourceTree_UndoDeleteFolderFailed", Resource);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    return undoResult;
                }
            }
            else
            {
                return Result.Fail($"Invalid deleted resource type for key: {Resource}");
            }

            return Result.Ok();
        }

        //
        // Static methods for scripting support.
        //

        public static void DeleteResource(ResourceKey resource)
        {
            var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
            commandService.Execute<IDeleteResourceCommand>(command => command.Resource = resource);
        }
    }
}
