using Celbridge.Commands;
using Celbridge.Core;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Entities.Commands;

public class PrintPropertyCommand : CommandBase, IPrintPropertyCommand
{
    private readonly ILogger<PrintPropertyCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ComponentKey ComponentKey { get; set; } = ComponentKey.Empty;
    public string PropertyPath { get; set; } = string.Empty;

    public PrintPropertyCommand(
        ILogger<PrintPropertyCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        var getResult = entityService.GetPropertyAsJson(ComponentKey, PropertyPath);
        if (getResult.IsFailure)
        {
            return Result.Fail().WithErrors(getResult);
        }

        var valueJSON = getResult.Value;
        _logger.LogInformation(valueJSON);

        await Task.CompletedTask;

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void PrintProperty(ResourceKey resource, int componentIndex, string propertyPath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();

        commandService.Execute<IPrintPropertyCommand>(command =>
        {
            command.ComponentKey = new ComponentKey(resource, componentIndex);
            command.PropertyPath = propertyPath;
        });
    }
}
