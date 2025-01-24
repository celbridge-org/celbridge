using Celbridge.Commands;
using Celbridge.Console.Views;
using Celbridge.Workspace;

namespace Celbridge.Console;

public class ClearCommand : CommandBase, IClearCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ClearCommand(IWorkspaceWrapper workspaceWrapper)
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

        var consolePanel = consoleService.ConsolePanel as ConsolePanel;
        Guard.IsNotNull(consolePanel);
            
        consolePanel.ViewModel.ClearLog();

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void Clear()
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IClearCommand>();
    }
}
