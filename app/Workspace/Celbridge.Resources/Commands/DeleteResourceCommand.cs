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
        _logger.LogDebug($"DeleteResourceCommand.ExecuteAsync started with {Resources.Count} resource(s)");

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            _logger.LogWarning("DeleteResourceCommand failed: Workspace is not loaded");
            return Result.Fail("Workspace is not loaded");
        }

        if (Resources.Count == 0)
        {
            _logger.LogDebug("DeleteResourceCommand: No resources to delete");
            return Result.Ok();
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var resourceOpService = workspaceService.ResourceService.OperationService;

        // Begin batch for single undo operation
        _logger.LogDebug("DeleteResourceCommand: Beginning batch operation");
        resourceOpService.BeginBatch();

        List<string> failedItems = new();

        try
        {
            foreach (var resource in Resources)
            {
                var resourcePath = resourceRegistry.GetResourcePath(resource);
                _logger.LogDebug($"DeleteResourceCommand: Deleting '{resource}' at path '{resourcePath}'");

                Result deleteResult;

                if (File.Exists(resourcePath))
                {
                    deleteResult = await resourceOpService.DeleteFileAsync(resourcePath);
                    _logger.LogDebug($"DeleteResourceCommand: DeleteFileAsync result: {(deleteResult.IsSuccess ? "Success" : deleteResult.Error)}");
                }
                else if (Directory.Exists(resourcePath))
                {
                    deleteResult = await resourceOpService.DeleteFolderAsync(resourcePath);
                    _logger.LogDebug($"DeleteResourceCommand: DeleteFolderAsync result: {(deleteResult.IsSuccess ? "Success" : deleteResult.Error)}");
                }
                else
                {
                    _logger.LogWarning($"Resource does not exist: {resource}");
                    failedItems.Add(resource.ResourceName);
                    continue;
                }

                if (deleteResult.IsFailure)
                {
                    _logger.LogError(deleteResult.Error);
                    failedItems.Add(resource.ResourceName);
                }
            }
        }
        finally
        {
            // Always commit batch - partial success is acceptable
            _logger.LogDebug("DeleteResourceCommand: Committing batch operation");
            resourceOpService.CommitBatch();
        }

        if (failedItems.Count > 0)
        {
            var failedList = string.Join(", ", failedItems);
            _logger.LogWarning($"DeleteResourceCommand completed with failures: {failedList}");

            // Notify the UI about the failure
            var message = new ResourceOperationFailedMessage(ResourceOperationType.Delete, failedItems);
            _messengerService.Send(message);

            return Result.Fail($"Failed to delete: {failedList}");
        }

        _logger.LogDebug("DeleteResourceCommand completed successfully");
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
