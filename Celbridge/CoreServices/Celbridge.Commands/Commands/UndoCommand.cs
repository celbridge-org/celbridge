namespace Celbridge.Commands;

public class UndoCommand : CommandBase, IUndoCommand
{
    public UndoStackName UndoStack { get; set; } = UndoStackName.Explorer;

    public override async Task<Result> ExecuteAsync()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Undo(UndoStack);

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void Undo()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IUndoCommand>(command =>
        {
            command.UndoStack = UndoStackName.Explorer;
        });
    }
}
