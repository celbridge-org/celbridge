using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Services.Project;

namespace Celbridge.Services.Commands;

public class CommandExecutor : ICommandExecutor
{
    private readonly IUserInterfaceService _userInterfaceService;

    public CommandExecutor(IUserInterfaceService userInterfaceService)
    {
        _userInterfaceService = userInterfaceService;
    }

    public bool CanExecuteCommand(CommandBase command)
    {
        if (command is UnloadProjectDataCommand)
        {
            return true;
        }

        if (_userInterfaceService.IsWorkspaceLoaded)
        {
            // Todo: Check if the workspace executor can execute the command
        }

        return false;
    }

    public async Task<Result> ExecuteCommand(CommandBase command)
    {
        if (command is UnloadProjectDataCommand unloadProjectDataCommand)
        {
            return await unloadProjectDataCommand.ExecuteAsync();
        }

        if (_userInterfaceService.IsWorkspaceLoaded)
        {
            // Todo: Execute the command via the workspace executor
        }

        return Result.Fail($"Command '{command}' is not supported by the command executor");
    }
}
