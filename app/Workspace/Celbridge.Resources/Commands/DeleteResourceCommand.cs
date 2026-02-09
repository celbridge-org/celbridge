using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class DeleteResourceCommand : CommandBase, IDeleteResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.ForceUpdateResources;

    public List<ResourceKey> Resources { get; set; } = new();

    private readonly ILogger<DeleteResourceCommand> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public DeleteResourceCommand(
        ILogger<DeleteResourceCommand> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace is not loaded");
        }

        if (Resources.Count == 0)
        {
            return Result.Ok();
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var resourceOpService = workspaceService.ResourceService.OperationService;

        // Begin batch for single undo operation
        resourceOpService.BeginBatch();

        List<string> failedItems = new();

        try
        {
            foreach (var resource in Resources)
            {
                var resourcePath = resourceRegistry.GetResourcePath(resource);

                Result deleteResult;

                if (File.Exists(resourcePath))
                {
                    deleteResult = await resourceOpService.DeleteFileAsync(resourcePath);
                }
                else if (Directory.Exists(resourcePath))
                {
                    deleteResult = await resourceOpService.DeleteFolderAsync(resourcePath);
                }
                else
                {
                    _logger.LogWarning($"Cannot delete resource because it does not exist: '{resource}'");
                    failedItems.Add(resource.ResourceName);
                    continue;
                }

                if (deleteResult.IsFailure)
                {
                    _logger.LogError($"Failed to delete resource '{resource}': {deleteResult.Error}");
                    failedItems.Add(resource.ResourceName);
                }
            }
        }
        finally
        {
            // Always commit batch - partial success is acceptable
            resourceOpService.CommitBatch();
        }

        if (failedItems.Count > 0)
        {
            var failedList = string.Join(", ", failedItems);

            // Notify the UI about the failure
            var message = new ResourceOperationFailedMessage(ResourceOperationType.Delete, failedItems);
            _messengerService.Send(message);

            return Result.Fail($"Failed to delete: {failedList}");
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
            command.Resources = new List<ResourceKey> { resource };
        });
    }

    public static void DeleteResources(List<ResourceKey> resources)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IDeleteResourceCommand>(command =>
        {
            command.Resources = resources;
        });
    }
}
