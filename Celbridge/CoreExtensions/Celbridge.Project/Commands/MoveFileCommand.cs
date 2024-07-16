using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Commands;

public class MoveFileCommand : CommandBase, IMoveFileCommand
{
    public override string UndoStackName => UndoStackNames.Project;

    public ResourceKey FromResourceKey { get; set; }
    public ResourceKey ToResourceKey { get; set; }

    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public MoveFileCommand(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        IProjectDataService projectDataService,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _projectDataService = projectDataService;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to move file. Workspace is not loaded");
        }

        var moveResult = await MoveFileInternal(FromResourceKey, ToResourceKey);
        if (moveResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_MoveFile");
            var messageString = _stringLocalizer.GetString("ResourceTree_MoveFileFailed", FromResourceKey, ToResourceKey);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);

            // The TreeView UI may now be out of sync with the actual project folder structure, so force a refresh.
            var message = new RequestResourceTreeUpdate();
            _messengerService.Send(message);
        }

        return moveResult;
    }

    public override async Task<Result> UndoAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to undo file move. Workspace is not loaded");
        }

        var moveResult = await MoveFileInternal(ToResourceKey, FromResourceKey);
        if (moveResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_MoveFile");
            var messageString = _stringLocalizer.GetString("ResourceTree_MoveFileFailed", ToResourceKey, FromResourceKey);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);

            // The TreeView UI may now be out of sync with the actual project folder structure, so force a refresh.
            var message = new RequestResourceTreeUpdate();
            _messengerService.Send(message);
        }

        return moveResult;
    }

    /// <summary>
    /// Move the file at resourceKeyA to resourceKeyB.
    /// </summary>
    private async Task<Result> MoveFileInternal(ResourceKey resourceKeyA, ResourceKey resourceKeyB)
    {
        if (resourceKeyA == resourceKeyB)
        {
            // Moving to the same resource key is a no-op
            return Result.Ok();
        }

        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the resource keys
        //

        if (resourceKeyA.IsEmpty ||
            resourceKeyB.IsEmpty)
        {
            return Result.Fail("Failed to move file. Resource key is empty.");
        }

        //
        // Move the file on disk
        //

        try
        {
            var projectFolderPath = loadedProjectData.ProjectFolderPath;
            var filePathA = Path.Combine(projectFolderPath, resourceKeyA);
            filePathA = Path.GetFullPath(filePathA); // Make separators consistent
            var filePathB = Path.Combine(projectFolderPath, resourceKeyB);
            filePathB = Path.GetFullPath(filePathB);

            if (!File.Exists(filePathA))
            {
                return Result.Fail($"Failed to move file. File does not exist: {filePathA}");
            }

            if (File.Exists(filePathB))
            {
                return Result.Fail($"Failed to move file. File already exists: {filePathB}");
            }

            var parentFolderPathB = Path.GetDirectoryName(filePathB);
            if (!Directory.Exists(parentFolderPathB))
            {
                // The parent folder of the target file path must exist before moving the file.
                // It would be easy to create the missing parent folder(s) here automatically, but it's hard to
                // undo this operation robustly so we just don't allow it.
                return Result.Fail($"Failed to move file. Target folder does not exist: {parentFolderPathB}");
            }

            File.Move(filePathA, filePathB);

            // Expand the parent folder of the moved file so that the user can see it in the tree view.
            var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;
            var newParentFolder = resourceKeyB.GetParent();
            if (!newParentFolder.IsEmpty)
            {
                resourceRegistry.SetFolderIsExpanded(newParentFolder, true);
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to move file. {ex.Message}");
        }

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    public static void MoveFile(string fromResourceKey, string toResourceKey)
    {
        if (Path.GetExtension(fromResourceKey) != Path.GetExtension(toResourceKey))
        {
            // If the "from" and "to" file extensions don't match then we assume that the user intended to
            // move the file to a folder rather than to a file with no extension.
            // This means that this command cannot be used to remove the file extension from a file.
            toResourceKey = Path.Combine(toResourceKey, Path.GetFileName(fromResourceKey));
        }

        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IMoveFileCommand>(command =>
        {
            command.FromResourceKey = fromResourceKey;
            command.ToResourceKey = toResourceKey;
        });
    }
}
