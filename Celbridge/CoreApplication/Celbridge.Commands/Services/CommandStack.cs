namespace Celbridge.Commands.Services;

public enum CommandStackOperation
{
    Undo,
    Redo
}

public record CommandStackItem(IExecutableCommand Command, CommandStackOperation Operation);

/// <summary>
/// Manages a stack of undo and redo commands.
/// </summary>
public class CommandStack
{
    private List<CommandStackItem> _operations = new ();

    public int GetUndoCount(string stackName)
    {
        return _operations.Count(item =>
            item.Operation == CommandStackOperation.Undo && 
            item.Command.StackName == stackName);
    }

    public int GetRedoCount(string stackName)
    {
        return _operations.Count(item =>
            item.Operation == CommandStackOperation.Redo &&
            item.Command.StackName == stackName);
    }

    public Result PushUndoCommand(IExecutableCommand command)
    {
        if (command.StackName == CommandStackNames.None)
        {
            return Result.Fail($"Failed to push undo command. The 'None' command stack may not contain commands.");
        }

        if (_operations.Any(item => item.Command.Id == command.Id))
        {
            return Result.Fail($"Failed to push undo command because a command with id '{command.Id}' already exists.");
        }

        // Add the item to the undo stack
        var item = new CommandStackItem(command, CommandStackOperation.Undo);
        _operations.Add(item);

        return Result.Ok();
    }

    public Result PushRedoCommand(IExecutableCommand command)
    {
        if (command.StackName == CommandStackNames.None)
        {
            return Result.Fail($"Failed to push redo command. The 'None' command stack may not contain commands.");
        }

        if (_operations.Any(item => item.Command.Id == command.Id))
        {
            return Result.Fail($"Failed to push undo command because a command with id '{command.Id}' already exists.");
        }

        var item = new CommandStackItem(command, CommandStackOperation.Redo);

        _operations.Add(item);

        return Result.Ok();
    }

    public Result<IExecutableCommand> PopUndoCommand(string stackName)
    {
        if (stackName == CommandStackNames.None)
        {
            return Result<IExecutableCommand>.Fail($"Failed to pop undo command. The 'None' command stack may not contain commands.");
        }

        //
        // Find the most recently added undo item in the specified stack
        //

        var index = _operations.FindLastIndex(item =>
            item.Operation == CommandStackOperation.Undo &&
            item.Command.StackName == stackName);

        if (index == -1)
        {
            return Result<IExecutableCommand>.Fail($"Failed to pop undo command from stack '{stackName}'. No commands found.");
        }

        //
        // Remove the undo item from the list
        //

        var item = _operations[index];
        _operations.RemoveAt(index);

        return Result<IExecutableCommand>.Ok(item.Command);
    }

    public Result<IExecutableCommand> PopRedoCommand(string stackName)
    {
        if (stackName == CommandStackNames.None)
        {
            return Result<IExecutableCommand>.Fail($"Failed to pop redo command. The 'None' command stack may not contain commands.");
        }

        //
        // Find the most recently added redo item in the specified stack
        //

        var index = _operations.FindLastIndex(item =>
            item.Operation == CommandStackOperation.Redo &&
            item.Command.StackName == stackName);

        if (index == -1)
        {
            return Result<IExecutableCommand>.Fail($"Failed to pop redo command from stack '{stackName}'. No commands found.");
        }

        //
        // Remove the redo item from the list
        //

        var item = _operations[index];
        _operations.RemoveAt(index);

        return Result<IExecutableCommand>.Ok(item.Command);
    }

    public Result ClearRedoCommands(string stackName)
    {
        if (stackName == CommandStackNames.None)
        {
            return Result.Fail($"Failed to clear redo commands. The 'None' command stack may not contain commands.");
        }

        _operations.RemoveAll(item => 
            item.Operation == CommandStackOperation.Redo && 
            item.Command.StackName == stackName);

        return Result.Ok();
    }
}
