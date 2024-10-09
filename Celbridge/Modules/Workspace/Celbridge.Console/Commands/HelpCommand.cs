using Celbridge.Commands;
using Celbridge.Foundation;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Console;

public class HelpCommand : CommandBase, IHelpCommand
{
    private ILogger<HelpCommand> _logger;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public string SearchTerm { get; set; } = string.Empty;

    public HelpCommand(
        ILogger<HelpCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace not loaded");
        }

        var scriptingService = _workspaceWrapper.WorkspaceService.ScriptingService;

        var helpText = scriptingService.GetHelpText(SearchTerm);

        _logger.LogInformation(helpText);

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void Help(string searchTerm)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IHelpCommand>(command =>
        {
            command.SearchTerm = searchTerm;
        });
    }

    public static void Help()
    {
        Help(string.Empty);
    }
}
