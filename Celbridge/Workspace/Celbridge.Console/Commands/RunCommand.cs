using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Console;

public class RunCommand : CommandBase, IRunCommand
{
    private ILogger<RunCommand> _logger;

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IDispatcher _dispatcher;

    public ResourceKey ScriptResource { get; set; }

    public RunCommand(
        IDispatcher dispatcher,
        ILogger<RunCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _dispatcher = dispatcher;
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace not loaded");
        }

        var consoleService = _workspaceWrapper.WorkspaceService.ConsoleService;

        var command = $"#!import \"{ScriptResource}\"";
        var executeResult = await consoleService.ConsolePanel.ExecuteCommand(command, false);
        if (executeResult.IsFailure)
        {
            return Result.Fail($"Failed to run script resource: {ScriptResource}")
                .WithErrors(executeResult);
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void Run(ResourceKey scriptResource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IRunCommand>(command =>
        {
            command.ScriptResource = scriptResource;
        });
    }
}
