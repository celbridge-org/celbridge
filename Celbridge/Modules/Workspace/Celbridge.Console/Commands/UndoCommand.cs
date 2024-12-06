using Celbridge.Commands;

namespace Celbridge.Console;

public class UndoCommand : CommandBase, IUndoCommand
{
    public override async Task<Result> ExecuteAsync()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Undo();

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void Undo()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IUndoCommand>();
    }
}
