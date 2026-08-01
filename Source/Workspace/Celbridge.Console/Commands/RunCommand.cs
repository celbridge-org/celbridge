using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Console;

public class RunCommand : CommandBase, IRunCommand
{
    private readonly ILogger<RunCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey ScriptResource { get; set; }

    public Guid SessionId { get; set; }

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
        await Task.CompletedTask;

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace not loaded");
        }

        // Passing the resource path (not ToString) keeps the run relative to the project root.
        var sessions = _workspaceWrapper.WorkspaceService.ConsoleService.Sessions;

        var sessionId = SessionId;
        if (sessionId == Guid.Empty)
        {
            // A programmatic caller that did not target a console runs in the first console that can run
            // the file type, by the same name order the Run menu lists.
            var extension = Path.GetExtension(ScriptResource.Path);
            var targets = sessions.GetRunTargets(extension);
            if (targets.Count == 0)
            {
                return Result.Fail($"No open console can run '{ScriptResource.Path}'");
            }
            sessionId = targets[0].SessionId;
        }

        var resolveResult = sessions.ResolveRunnerInvocation(sessionId, ScriptResource.Path, Arguments);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to run script '{ScriptResource.Path}'")
                .WithErrors(resolveResult);
        }
        var invocation = resolveResult.Value;

        var submitResult = sessions.SubmitInvocation(sessionId, invocation);
        if (submitResult.IsFailure)
        {
            return Result.Fail($"Failed to run script '{ScriptResource.Path}'")
                .WithErrors(submitResult);
        }

        _logger.LogDebug("Run script '{Script}' in console session {SessionId}", ScriptResource.Path, sessionId);

        return Result.Ok();
    }
}
