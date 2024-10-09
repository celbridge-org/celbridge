using Celbridge.Commands;
using Celbridge.Console.Views;
using Celbridge.Foundation;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

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

        var consoleService = _workspaceWrapper.WorkspaceService.ConsoleService;

        var consolePanel = consoleService.ConsolePanel as ConsolePanel;
        Guard.IsNotNull(consolePanel);
            
        consolePanel.ViewModel.ClearHistory();

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void ClearHistory()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IClearHistoryCommand>();
    }
}
