using Celbridge.Commands;
using Celbridge.UserInterface;

namespace Celbridge.Console;

public class RedoCommand : CommandBase, IRedoCommand
{
    public override async Task<Result> ExecuteAsync()
    {
        var undoService = ServiceLocator.AcquireService<IUndoService>();
        undoService.Redo();

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void Redo()
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IRedoCommand>();
    }
}
