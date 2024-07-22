namespace Celbridge.Commands.Services;

public enum UndoStackOperation
{
    Undo,
    Redo
}

public record UndoStackItem(IExecutableCommand Command, UndoStackOperation Operation);

/// <summary>
/// Manages a stack of undo and redo commands.
/// </summary>
public class UndoStack
{
    private List<UndoStackItem> _operations = new();

    public int GetUndoCount(string undoStackName)
    {
        return _operations.Count(item =>
            item.Operation == UndoStackOperation.Undo && 
            item.Command.UndoStackName == undoStackName);
    }

    public int GetRedoCount(string undoStackName)
    {
        return _operations.Count(item =>
            item.Operation == UndoStackOperation.Redo &&
            item.Command.UndoStackName == undoStackName);
    }

    public Result PushUndoCommand(IExecutableCommand command)
    {
        if (command.UndoStackName == UndoStackNames.None)
        {
            return Result.Fail($"Failed to push undo command. The 'None' undo stack may not contain commands.");
        }

        if (_operations.Any(item => item.Command.CommandId == command.CommandId))
        {
            return Result.Fail($"Failed to push undo command because a command with id '{command.CommandId}' already exists.");
        }

        // Add the item to the undo stack
        var item = new UndoStackItem(command, UndoStackOperation.Undo);
        _operations.Add(item);

        return Result.Ok();
    }

    public Result PushRedoCommand(IExecutableCommand command)
    {
        if (command.UndoStackName == UndoStackNames.None)
        {
            return Result.Fail($"Failed to push redo command. The 'None' undo stack may not contain commands.");
        }

        if (_operations.Any(item => item.Command.CommandId == command.CommandId))
        {
            return Result.Fail($"Failed to push undo command because a command with id '{command.CommandId}' already exists.");
        }

        var item = new UndoStackItem(command, UndoStackOperation.Redo);

        _operations.Add(item);

        return Result.Ok();
    }

    public Result<IExecutableCommand> PopUndoCommand(string undoStackName)
    {
        if (undoStackName == UndoStackNames.None)
        {
            return Result<IExecutableCommand>.Fail($"Failed to pop undo command. The 'None' undo stack may not contain commands.");
        }

        //
        // Find the most recently added undo item in the specified undo stack
        //

        var index = _operations.FindLastIndex(item =>
            item.Operation == UndoStackOperation.Undo &&
            item.Command.UndoStackName == undoStackName);

        if (index == -1)
        {
            return Result<IExecutableCommand>.Fail($"Failed to pop undo command from stack '{undoStackName}'. No commands found.");
        }

        //
        // Remove the undo item from the list
        //

        var item = _operations[index];
        _operations.RemoveAt(index);

        return Result<IExecutableCommand>.Ok(item.Command);
    }

    public Result<IExecutableCommand> PopRedoCommand(string undoStackName)
    {
        if (undoStackName == UndoStackNames.None)
        {
            return Result<IExecutableCommand>.Fail($"Failed to pop redo command. The 'None' undo stack may not contain commands.");
        }

        //
        // Find the most recently added redo item in the specified undo stack
        //

        var index = _operations.FindLastIndex(item =>
            item.Operation == UndoStackOperation.Redo &&
            item.Command.UndoStackName == undoStackName);

        if (index == -1)
        {
            return Result<IExecutableCommand>.Fail($"Failed to pop redo command from undo stack '{undoStackName}'. No commands found.");
        }

        //
        // Remove the redo item from the list
        //

        var item = _operations[index];
        _operations.RemoveAt(index);

        return Result<IExecutableCommand>.Ok(item.Command);
    }

    public Result ClearRedoCommands(string undoStackName)
    {
        if (undoStackName == UndoStackNames.None)
        {
            return Result.Fail($"Failed to clear redo commands. The 'None' undo stack may not contain commands.");
        }

        _operations.RemoveAll(item => 
            item.Operation == UndoStackOperation.Redo && 
            item.Command.UndoStackName == undoStackName);

        return Result.Ok();
    }
}
