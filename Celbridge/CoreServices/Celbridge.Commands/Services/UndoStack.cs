using Celbridge.Foundation;
using CommunityToolkit.Diagnostics;

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

    public int GetUndoCount(UndoStackName undoStackName)
    {
        return _operations.Count(item =>
            item.Operation == UndoStackOperation.Undo && 
            item.Command.UndoStackName == undoStackName);
    }

    public int GetRedoCount(UndoStackName undoStackName)
    {
        return _operations.Count(item =>
            item.Operation == UndoStackOperation.Redo &&
            item.Command.UndoStackName == undoStackName);
    }

    public Result PushUndoCommand(IExecutableCommand command)
    {
        if (command.UndoStackName == UndoStackName.None)
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
        if (command.UndoStackName == UndoStackName.None)
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

    public Result<IEnumerable<IExecutableCommand>> PopCommands(UndoStackName undoStackName, UndoStackOperation operation)
    {
        if (undoStackName == UndoStackName.None)
        {
            return Result<IEnumerable<IExecutableCommand>>.Fail($"Failed to pop commands for '{operation}'. The 'None' undo stack may not contain commands.");
        }

        var commands = new List<IExecutableCommand>();

        // Iterate in reverse to find and collect all commands with the same undo group id
        for (int i = _operations.Count - 1; i >= 0; i--)
        {
            var item = _operations[i];

            if (item.Command.UndoStackName == undoStackName &&
                item.Operation == operation)
            {
                var undoGroupId = item.Command.UndoGroupId;

                if (!undoGroupId.IsValid)
                {
                    // An invalid undoGroupId indicates that the command is not part of a group.
                    // Pop this command on its own and return it.
                    commands.Add(item.Command);
                    _operations.RemoveAt(i);
                    return Result<IEnumerable<IExecutableCommand>>.Ok(commands);
                }

                // Collect all commands with the same undo group id (including the one we found above)
                for (int j = _operations.Count - 1; j >= 0; j--)
                {
                    var groupItem = _operations[j];

                    if (groupItem.Command.UndoGroupId == undoGroupId)
                    {
                        // Something has gone badly wrong if commands with the same undo group id have ended up
                        // in different undo stacks.
                        Guard.IsTrue(groupItem.Command.UndoStackName == undoStackName);
                        Guard.IsTrue(groupItem.Operation == operation);

                        commands.Add(_operations[j].Command);
                        _operations.RemoveAt(j);
                    }
                }

                break;
            }
        }

        if (commands.Count == 0)
        {
            return Result<IEnumerable<IExecutableCommand>>.Fail($"No commands found in '{undoStackName}' undo stack.");
        }

        return Result<IEnumerable<IExecutableCommand>>.Ok(commands);
    }

    public Result ClearRedoCommands(UndoStackName undoStackName)
    {
        if (undoStackName == UndoStackName.None)
        {
            return Result.Fail($"Failed to clear redo commands. The 'None' undo stack may not contain commands.");
        }

        _operations.RemoveAll(item => 
            item.Operation == UndoStackOperation.Redo && 
            item.Command.UndoStackName == undoStackName);

        return Result.Ok();
    }
}
