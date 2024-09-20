namespace Celbridge.Scripting;

/// <summary>
/// The scripting service supports the execution of C#, F#, Python, etc. scripts.
/// </summary>
public interface IScriptingService
{
    /// <summary>
    /// Create a new ScriptContext to maintain state across command executions.
    /// </summary>
    Task<IScriptContext> CreateScriptContext();

    /// <summary>
    /// Add a command to be executed when the ScriptContext is created.
    /// </summary>
    void AddContextSetupCommand(string command);
}
