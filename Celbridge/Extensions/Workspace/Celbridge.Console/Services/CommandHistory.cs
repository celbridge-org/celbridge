using Celbridge.Workspace;

namespace Celbridge.Console.Services;

public class CommandHistory : ICommandHistory
{
    private IWorkspaceService _workspaceService;

    private List<string> _commands = new();
    private int _commandIndex;

    public int MaxHistorySize { get; set; } = 100;

    public int NumCommands => _commands.Count;

    public CommandHistory(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceService = workspaceWrapper.WorkspaceService;
    }

    public void Clear()
    {
        _commands.Clear();
        _commandIndex = 0;
    }

    public async Task Save()
    {
        var workspaceSettings = _workspaceService.WorkspaceSettings;

        await workspaceSettings.SetPropertyAsync("commandHistory", _commands);
    }

    public async Task Load()
    {
        var workspaceSettings = _workspaceService.WorkspaceSettings;

        var commands = await workspaceSettings.GetPropertyAsync<List<string>>("commandHistory", null);
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

        // Remove any previously entered identical command.
        // The same commands tend to be entered repeatedly, so this reduces clutter.
        _commands.RemoveAll(c => c == commandText);

        _commands.Add(commandText);

        // Constrain the size of the command history buffer
        int removeCount = _commands.Count - MaxHistorySize;
        if (removeCount > 0)
        {
            _commands.RemoveRange(0, removeCount);
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
