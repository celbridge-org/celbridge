using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Console;

public class RunCommand : CommandBase, IRunCommand
{
    private ILogger<RunCommand> _logger;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey ScriptResource { get; set; }

    public string Arguments { get; set; } = string.Empty;

    public RunCommand(
        ILogger<RunCommand> logger,
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

        var consoleService = _workspaceWrapper.WorkspaceService.ConsoleService;

        var command = $"%run \"{ScriptResource}\"";
        if (string.IsNullOrEmpty(Arguments))
        {
            command += " " + Arguments;
        }

        consoleService.RunCommand(command);

        _logger.LogDebug($"Run script: {command}");

        await Task.CompletedTask;

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void Run(ResourceKey scriptResource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IRunCommand>(command =>
        {
            command.ScriptResource = scriptResource;
        });
    }
}
