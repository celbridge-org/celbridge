namespace Celbridge.Scripting;

public enum ExecutionStatus
{
    NotStarted,
    InProgress,
    Finished,
    Error
}

/// <summary>
/// Manages state for an executing scripting command.
/// </summary>
public interface IScriptExecutor
{
    IScriptContext ScriptContext { get; init; }

    string Command { get; init; }

    ExecutionStatus Status { get; }

    event Action<string>? OnOutput;

    event Action<string>? OnError;

    public abstract Task<Result> ExecuteAsync();
}