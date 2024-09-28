using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Console.Services;

public class CommandHistory : ICommandHistory
{
    private IWorkspaceDataService? _workspaceDataService;

    private List<string> _commands = new();
    private int _commandIndex;

    public uint MaxHistorySize { get; set; } = 100;

    public uint NumCommands => (uint)_commands.Count;

    public CommandHistory()
    {}

    public CommandHistory(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceDataService = workspaceWrapper.WorkspaceService.WorkspaceDataService;
    }

    public void ClearCommandHistory()
    {
        _commands.Clear();
        _commandIndex = 0;
    }

    public async Task SaveCommandHistory()
    {
        Guard.IsNotNull(_workspaceDataService);

        IWorkspaceData workspaceData = _workspaceDataService.LoadedWorkspaceData!;

        await workspaceData.SetPropertyAsync<List<string>>("commandHistory", _commands);
    }

    public async Task LoadCommandHistory()
    {
        Guard.IsNotNull(_workspaceDataService);

        IWorkspaceData workspaceData = _workspaceDataService.LoadedWorkspaceData!;

        var commands = await workspaceData.GetPropertyAsync<List<string>>("commandHistory", null);
        if (commands is not null)
        {
            _commands.ReplaceWith(commands);

            // Set the command index to one after the last entry
            _commandIndex = _commands.Count;
        }
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
        while (_commands.Count > MaxHistorySize)
        {
            // This is potentially expensive, could replace with a linked list implementation.
            _commands.RemoveAt(0);
        }

        // Set the command index to one after the last entry
        _commandIndex = _commands.Count;
    }

    public Result<string> GetSelectedCommand()
    {
        if (_commands.Count == 0)
        {
            return Result<string>.Fail("Failed to get current command because command history is empty");
        }

        if (_commandIndex >= _commands.Count)
        {
            // Command index is at the end of the command history
            return Result<string>.Ok(string.Empty);
        }

        var command = _commands[_commandIndex];
        return Result<string>.Ok(command);
    }

    public bool CanSelectNextCommand => _commands.Count > 0 && _commandIndex < _commands.Count;

    public Result SelectNextCommand()
    {
        if (!CanSelectNextCommand)
        {
            return Result.Fail("Failed to move to next command in history because history is empty or already at end.");
        }

        _commandIndex++;

        return Result.Ok();
    }

    public bool CanSelectPreviousCommand => _commands.Count > 0 && _commandIndex > 0;

    public Result SelectPreviousCommand()
    {
        if (!CanSelectPreviousCommand)
        {
            return Result.Fail("Failed to move to previous command in history because history is empty or already at start.");
        }

        _commandIndex--;

        return Result.Ok();
    }
}
