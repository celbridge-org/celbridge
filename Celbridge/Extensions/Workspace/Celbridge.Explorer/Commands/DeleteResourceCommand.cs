using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Celbridge.Explorer.Services;

namespace Celbridge.Explorer.Commands
{
    public class DeleteResourceCommand : CommandBase, IDeleteResourceCommand
    {
        public override UndoStackName UndoStackName => UndoStackName.Explorer;
        public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

        public ResourceKey Resource { get; set; }

        private ResourceArchiver _archiver;

        private readonly IWorkspaceWrapper _workspaceWrapper;
        private readonly IDialogService _dialogService;
        private readonly IStringLocalizer _stringLocalizer;

        public DeleteResourceCommand(
            IServiceProvider serviceProvider,
            IWorkspaceWrapper workspaceWrapper,
            IDialogService dialogService,
            IStringLocalizer stringLocalizer)
        {
            _workspaceWrapper = workspaceWrapper;
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
                var archiveResult = await _archiver.ArchiveResourceAsync(Resource);
                if (archiveResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFile");
                    var messageString = _stringLocalizer.GetString("ResourceTree_DeleteFileFailed", Resource);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    return archiveResult;
                }
            }
            else if (resource is IFolderResource)
            {
                var archiveResult = await _archiver.ArchiveResourceAsync(Resource);
                if (archiveResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFolder");
                    var messageString = _stringLocalizer.GetString("ResourceTree_DeleteFolderFailed", Resource);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    return archiveResult;
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
            if (_archiver.ArchivedResourceType == ResourceType.File)
            {
                var unarchiveResult = await _archiver.UnarchiveResourceAsync();
                if (unarchiveResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFile");
                    var messageString = _stringLocalizer.GetString("ResourceTree_UndoDeleteFileFailed", Resource);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    return unarchiveResult;
                }
            }
            else if (_archiver.ArchivedResourceType == ResourceType.Folder)
            {
                var unarchiveResult = await _archiver.UnarchiveResourceAsync();
                if (unarchiveResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFolder");
                    var messageString = _stringLocalizer.GetString("ResourceTree_UndoDeleteFolderFailed", Resource);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    return unarchiveResult;
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
