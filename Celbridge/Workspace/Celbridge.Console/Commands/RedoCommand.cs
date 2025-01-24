using Celbridge.Commands;

namespace Celbridge.Console;

public class RedoCommand : CommandBase, IRedoCommand
{
    public override async Task<Result> ExecuteAsync()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Redo();

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void Redo()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IRedoCommand>();
    }
}
