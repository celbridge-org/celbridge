using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class AddResourceCommand : CommandBase, IAddResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.ForceUpdateResources;

    public ResourceType ResourceType { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public ResourceKey DestResource { get; set; }
    public bool OpenAfterAdding { get; set; } = false;

    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly AddResourceHelper _addResourceHelper;

    public AddResourceCommand(
        IMessengerService messengerService,
        ICommandService commandService,
        AddResourceHelper addResourceHelper)
    {
        _messengerService = messengerService;
        _commandService = commandService;
        _addResourceHelper = addResourceHelper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var addResult = await _addResourceHelper.AddResourceAsync(ResourceType, SourcePath, DestResource);

        if (addResult.IsFailure)
        {
            // Notify the UI about the failure
            var failedItems = new List<string> { DestResource.ResourceName };
            var message = new ResourceOperationFailedMessage(ResourceOperationType.Create, failedItems);
            _messengerService.Send(message);

            return addResult;
        }

        _commandService.Execute<ISelectResourceCommand>(command =>
        {
            command.Resource = DestResource;
        });

        if (OpenAfterAdding)
        {
            _commandService.Execute<IOpenDocumentCommand>(command =>
            {
                command.FileResource = DestResource;
                command.ForceReload = false;
            });

            _commandService.Execute<ISelectDocumentCommand>(command =>
            {
                command.FileResource = DestResource;
            });
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static async void AddFile(string sourcePath, ResourceKey destResource)
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            throw new InvalidOperationException("Failed to add resource because workspace is not loaded");
        }

        // If the destination resource is a existing folder, resolve the destination resource to a file in
        // that folder with the same name as the source file.
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resolvedDestResource = resourceRegistry.ResolveSourcePathDestinationResource(sourcePath, destResource);

        var commandService = ServiceLocator.AcquireService<ICommandService>();

        await commandService.ExecuteAsync<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.SourcePath = sourcePath;
            command.DestResource = resolvedDestResource;
            command.OpenAfterAdding = true;
        });
    }

    public static void AddFile(ResourceKey destResource)
    {
        AddFile(new ResourceKey(), destResource);
    }

    public static void AddFolder(string sourcePath, ResourceKey destResource)
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            throw new InvalidOperationException("Failed to add resource because workspace is not loaded");
        }

        // If the destination resource is a existing folder, resolve the destination resource to a folder in
        // that folder with the same name as the source folder.
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resolvedDestResource = resourceRegistry.ResolveSourcePathDestinationResource(sourcePath, destResource);

        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.SourcePath = sourcePath;
            command.DestResource = resolvedDestResource;
        });
    }

    public static void AddFolder(ResourceKey destResource)
    {
        AddFolder(new ResourceKey(), destResource);
    }
}
