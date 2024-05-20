namespace Celbridge.Scripting;

public enum ExecutionStatus
{
    NotStarted,
    InProgress,
    Finished,
    Error
}

public abstract class ScriptExecutionContext
{
    public string Command { get; init; }

    public event Action<string>? OnOutput;

    public event Action<string>? OnError;

    public ScriptExecutionContext(string command)
    {
        Command = command;
    }

    public ExecutionStatus Status { get; protected set; } = ExecutionStatus.NotStarted;

    public abstract Task<Result> ExecuteAsync();

    protected virtual void WriteOutput(string output)
    {
        OnOutput?.Invoke(output);
    }

    protected virtual void WriteError(string error)
    {
        OnError?.Invoke(error);
    }
}