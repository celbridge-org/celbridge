using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting.FakeScript;

public class FakeScriptExecutionContext : ScriptExecutionContext
{
    public FakeScriptExecutionContext(IScriptContext scriptContext, string command)
        : base(scriptContext, command)
    {}

    public override async Task<Result> ExecuteAsync()
    {
        if (string.IsNullOrEmpty(Command))
        {
            return Result.Fail("Command cannot be null or empty.");
        }

        if (Status != ExecutionStatus.NotStarted)
        {
            return Result.Fail($"Failed to execute ScriptExecutionContext because it is in the '{Status}' status.");
        }

        // Mirror the command to the output
        WriteOutput(Command);

        Status = ExecutionStatus.Finished;

        await Task.CompletedTask;

        return Result.Ok();
    }
}