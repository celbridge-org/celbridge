using Celbridge.Commands;
using Celbridge.UserInterface;

namespace Celbridge.Console;

public class UndoCommand : CommandBase, IUndoCommand
{
    public override async Task<Result> ExecuteAsync()
    {
        var undoService = ServiceLocator.AcquireService<IUndoService>();

        undoService.Undo();

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void Undo()
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IUndoCommand>();
    }
}
