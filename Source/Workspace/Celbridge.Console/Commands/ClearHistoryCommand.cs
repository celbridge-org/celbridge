using Celbridge.Commands;
using Celbridge.Console.Views;
using Celbridge.Workspace;

namespace Celbridge.Console;

public class ClearHistoryCommand : CommandBase, IClearHistoryCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ClearHistoryCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace not loaded");
        }

        var consolePanel = _workspaceWrapper.WorkspaceService.ConsolePanel as ConsolePanel;
        Guard.IsNotNull(consolePanel);
            
        // consolePanel.ViewModel.ClearHistory();

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void ClearHistory()
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IClearHistoryCommand>();
    }
}
