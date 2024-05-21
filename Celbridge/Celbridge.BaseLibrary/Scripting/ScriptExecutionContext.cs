using Celbridge.BaseLibrary.Scripting;

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
    protected IScriptContext ScriptContext { get; init; }

    public string Command { get; init; }

    public event Action<string>? OnOutput;

    public event Action<string>? OnError;

    public ScriptExecutionContext(IScriptContext scriptContext, string command)
    {
        ScriptContext = scriptContext;
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