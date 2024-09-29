namespace Celbridge.Commands;

public class RedoCommand : CommandBase, IRedoCommand
{
    public UndoStackName UndoStack { get; set; } = UndoStackName.Explorer;

    public override async Task<Result> ExecuteAsync()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Redo(UndoStack);

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void Redo()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IRedoCommand>(command =>
        {
            command.UndoStack = UndoStackName.Explorer;
        });
    }
}
