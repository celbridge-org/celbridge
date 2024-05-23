namespace Celbridge.BaseLibrary.Scripting;

/// <summary>
/// The scripting service supports the execution of C#, F#, Python, etc. scripts.
/// </summary>
public interface IScriptingService
{
    Task<IScriptContext> CreateScriptContext();

    void AddContextSetupCommand(string command);
}
