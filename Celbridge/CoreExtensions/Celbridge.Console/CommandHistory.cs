using Celbridge.BaseLibrary.Console;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Console;

public class CommandHistory : ICommandHistory
{
    private List<string> _commands = new();
    private int _commandIndex;

    public uint HistorySizeMax { get; set; } = 200;

    public uint HistorySize => (uint)_commands.Count;

    public void Clear()
    {
        _commands.Clear();
        _commandIndex = 0;
    }

    public void AddCommand(string commandText)
    {
        if (string.IsNullOrEmpty(commandText))
        {
            // Adding an empty command to the history is a no-op
            return;
        }

        bool repeatedCommand = _commands.Count > 0 && _commands[_commands.Count - 1] == commandText;
        if (!repeatedCommand)
        {
            // If the same command is entered repeatedly, only store the first instance
            _commands.Add(commandText);
        }

        // Limit how big the history can grow
        while (_commands.Count > HistorySizeMax) 
        {
            // This is potentially expensive, could replace with a linked list implementation.
            _commands.RemoveAt(0);
        }

        // Set the current command to the most recently added command
        _commandIndex = Math.Max(0, _commands.Count - 1);
    }

    public Result<string> GetCurrentCommand()
    {
        if (_commands.Count == 0)
        {
            return Result<string>.Fail("Failed to get current command because command history is empty");
        }

        Guard.IsTrue(_commandIndex >= 0 && _commandIndex < _commands.Count);

        var command = _commands[_commandIndex];

        return Result<string>.Ok(command);
    }

    public bool CanMoveToNextCommand => _commands.Count > 0 && _commandIndex < _commands.Count - 1;

    public Result MoveToNextCommand()
    {
        if (!CanMoveToNextCommand)
        {
            return Result.Fail("Failed to move to next command in history because history is empty or already at end.");
        }

        _commandIndex++;

        return Result.Ok();
    }

    public bool CanMoveToPreviousCommand => _commands.Count > 0 && _commandIndex > 0;

    public Result MoveToPreviousCommand()
    {
        if (!CanMoveToPreviousCommand)
        {
            return Result.Fail("Failed to move to previous command in history because history is empty or already at start.");
        }

        _commandIndex--;

        return Result.Ok();
    }
}
