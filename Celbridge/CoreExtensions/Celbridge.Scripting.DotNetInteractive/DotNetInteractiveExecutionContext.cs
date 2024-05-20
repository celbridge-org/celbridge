namespace Celbridge.Scripting.DotNetInteractive;

public class DotNetInteractiveExecutionContext : ScriptExecutionContext
{
    public DotNetInteractiveExecutionContext(string command)
        : base(command)
    { }

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

        // Todo: Execute the script using C# Interactive

        // Mirror the command to the output
        WriteOutput(Command);

        Status = ExecutionStatus.Finished;

        await Task.CompletedTask;

        return Result.Ok();
    }
}