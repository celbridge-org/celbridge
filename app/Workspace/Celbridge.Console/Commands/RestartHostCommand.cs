using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Console;

public class RestartHostCommand : CommandBase, IRestartHostCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public RestartHostCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace not loaded");
        }

        var consoleService = _workspaceWrapper.WorkspaceService.ConsoleService;

        // Reinitialize the terminal window
        var result = await consoleService.InitializeTerminalWindow();
        if (result.IsFailure)
        {
            return result;
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void RestartHost()
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IRestartHostCommand>();
    }
}
