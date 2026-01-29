using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Dialog;
using Celbridge.Explorer;
using Celbridge.Projects;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Resources.Commands;

public class CopyResourceCommand : CommandBase, ICopyResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.RequestUpdateResources;

    public ResourceKey SourceResource { get; set; }
    public ResourceKey DestResource { get; set; }
    public DataTransferMode TransferMode { get; set; }
    public bool ExpandCopiedFolder { get; set; }

    private readonly IProjectService _projectService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;

    public CopyResourceCommand(
        IProjectService projectService,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _projectService = projectService;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
        _workspaceWrapper = workspaceWrapper;
        _commandService = commandService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Workspace is not loaded");
        }

        var project = _projectService.CurrentProject;
        Guard.IsNotNull(project);

        var projectFolderPath = project.ProjectFolderPath;
        if (string.IsNullOrEmpty(projectFolderPath))
        {
            return Result.Fail("Project folder path is empty.");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var resourceOpService = workspaceService.ResourceService.OperationService;

        // Resolve destination to handle folder drops
        var resolvedDestResource = resourceRegistry.ResolveDestinationResource(SourceResource, DestResource);

        // Convert resource keys to paths
        var sourcePath = Path.GetFullPath(Path.Combine(projectFolderPath, SourceResource));
        var destPath = Path.GetFullPath(Path.Combine(projectFolderPath, resolvedDestResource));

        // Determine resource type
        bool isFile = File.Exists(sourcePath);
        bool isFolder = Directory.Exists(sourcePath);

        if (!isFile && !isFolder)
        {
            await OnOperationFailed();
            return Result.Fail($"Resource does not exist: {sourcePath}");
        }

        Result result;

        if (isFile)
        {
            if (TransferMode == DataTransferMode.Copy)
            {
                result = await resourceOpService.CopyFileAsync(sourcePath, destPath);
            }
            else
            {
                result = await resourceOpService.MoveFileAsync(sourcePath, destPath);
            }
        }
        else
        {
            if (TransferMode == DataTransferMode.Copy)
            {
                result = await resourceOpService.CopyFolderAsync(sourcePath, destPath);
            }
            else
            {
                result = await resourceOpService.MoveFolderAsync(sourcePath, destPath);
            }
        }

        if (result.IsFailure)
        {
            await OnOperationFailed();
            return result;
        }

        // Expand destination folder
        var newParentFolder = resolvedDestResource.GetParent();
        if (!newParentFolder.IsEmpty)
        {
            _commandService.Execute<IExpandFolderCommand>(command =>
            {
                command.FolderResource = newParentFolder;
                command.Expanded = true;
            });
        }

        if (ExpandCopiedFolder && isFolder)
        {
            _commandService.Execute<IExpandFolderCommand>(command =>
            {
                command.FolderResource = resolvedDestResource;
                command.Expanded = true;
            });
        }

        return Result.Ok();
    }

    private async Task OnOperationFailed()
    {
        var titleKey = TransferMode == DataTransferMode.Copy ? "ResourceTree_CopyResource" : "ResourceTree_MoveResource";
        var messageKey = TransferMode == DataTransferMode.Copy ? "ResourceTree_CopyResourceFailed" : "ResourceTree_MoveResourceFailed";

        var titleString = _stringLocalizer.GetString(titleKey);
        var messageString = _stringLocalizer.GetString(messageKey, SourceResource, DestResource);
        await _dialogService.ShowAlertDialogAsync(titleString, messageString);
    }

    //
    // Static methods for scripting support.
    //

    public static void CopyResource(ResourceKey sourceResource, ResourceKey destResource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICopyResourceCommand>(command =>
        {
            command.SourceResource = sourceResource;
            command.DestResource = destResource;
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    public static void MoveResource(ResourceKey sourceResource, ResourceKey destResource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICopyResourceCommand>(command =>
        {
            command.SourceResource = sourceResource;
            command.DestResource = destResource;
            command.TransferMode = DataTransferMode.Move;
        });
    }
}
