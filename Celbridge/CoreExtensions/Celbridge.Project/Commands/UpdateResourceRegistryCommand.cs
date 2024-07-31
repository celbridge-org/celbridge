using Celbridge.Commands;
using Celbridge.Resources;

namespace Celbridge.Projects.Commands;

public class UpdateResourceRegistryCommand : CommandBase, IUpdateResourceRegistryCommand
{
    private readonly IMessengerService _messengerService;

    public UpdateResourceRegistryCommand(
        IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var message = new RequestResourceRegistryUpdateMessage();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //
    public static void UpdateResourceRegistry()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IUpdateResourceRegistryCommand>();
    }
}
