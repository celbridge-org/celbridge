using Celbridge.Commands;
using Celbridge.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Workspace.Commands;

public class SaveWorkspaceStateCommand : CommandBase, ISaveWorkspaceStateCommand
{
    private readonly IMessengerService _messengerService;

    public SaveWorkspaceStateCommand(
        IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var message = new RequestSaveWorkspaceStateMessage();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void SaveWorkspaceState()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ISaveWorkspaceStateCommand>();
    }
}
