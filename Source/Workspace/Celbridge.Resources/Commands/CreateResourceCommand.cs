using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class CreateResourceCommand : CommandBase, ICreateResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceType ResourceType { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public ResourceKey DestResource { get; set; }
    public bool OpenAfterCreating { get; set; } = false;

    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly CreateResourceHelper _createResourceHelper;

    public CreateResourceCommand(
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        CreateResourceHelper createResourceHelper)
    {
        _messengerService = messengerService;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _createResourceHelper = createResourceHelper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // .cel is reserved for project metadata sidecars. Refuse to create
        // files in the namespace. The dialog-level validator catches the same
        // case through the UI. This guard catches programmatic / scripted
        // callers that bypass the dialog.
        if (ResourceType == ResourceType.File
            && _workspaceWrapper.WorkspaceService.ResourceService.Sidecars.IsSidecarFileName(DestResource.ResourceName))
        {
            var reservationFailure = Result.Fail(
                $"Cannot create file '{DestResource}': the .cel extension is reserved for project metadata sidecars.");

            List<string> failedReservedItems = [DestResource.ResourceName];
            var reservationMessage = new ResourceOperationFailedMessage(ResourceOperationType.Create, failedReservedItems);
            _messengerService.Send(reservationMessage);

            return reservationFailure;
        }

        var createResult = await _createResourceHelper.CreateResourceAsync(ResourceType, SourcePath, DestResource);

        if (createResult.IsFailure)
        {
            // Notify the UI about the failure
            List<string> failedItems = [DestResource.ResourceName];
            var message = new ResourceOperationFailedMessage(ResourceOperationType.Create, failedItems);
            _messengerService.Send(message);

            return createResult;
        }

        _commandService.Execute<ISelectResourceCommand>(command =>
        {
            command.Resource = DestResource;
        });

        if (OpenAfterCreating)
        {
            _commandService.Execute<IOpenDocumentCommand>(command =>
            {
                command.FileResource = DestResource;
                command.ForceReload = false;
            });

            _commandService.Execute<IActivateDocumentCommand>(command =>
            {
                command.FileResource = DestResource;
            });
        }

        return Result.Ok();
    }

    public static async void NewFile(string sourcePath, ResourceKey destResource)
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            throw new InvalidOperationException("Failed to create resource because workspace is not loaded");
        }

        // If the destination resource is a existing folder, resolve the destination resource to a file in
        // that folder with the same name as the source file.
        var transferService = workspaceWrapper.WorkspaceService.ResourceService.Transfers;
        var resolvedDestResource = transferService.ResolveSourcePathDestinationResource(sourcePath, destResource);

        var commandService = ServiceLocator.AcquireService<ICommandService>();

        await commandService.ExecuteAsync<ICreateResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.SourcePath = sourcePath;
            command.DestResource = resolvedDestResource;
            command.OpenAfterCreating = true;
        });
    }

    public static void NewFile(ResourceKey destResource)
    {
        NewFile(new ResourceKey(), destResource);
    }

    public static void NewFolder(string sourcePath, ResourceKey destResource)
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            throw new InvalidOperationException("Failed to create resource because workspace is not loaded");
        }

        // If the destination resource is a existing folder, resolve the destination resource to a folder in
        // that folder with the same name as the source folder.
        var transferService = workspaceWrapper.WorkspaceService.ResourceService.Transfers;
        var resolvedDestResource = transferService.ResolveSourcePathDestinationResource(sourcePath, destResource);

        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICreateResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.SourcePath = sourcePath;
            command.DestResource = resolvedDestResource;
        });
    }

    public static void NewFolder(ResourceKey destResource)
    {
        NewFolder(new ResourceKey(), destResource);
    }
}
