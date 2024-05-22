using Celbridge.Scripting;

namespace Celbridge.BaseLibrary.Scripting;

/// <summary>
/// A script context is an environment for script execution.
/// It can be used to execute scripts in C#, F#, Python, etc. and to maintain state between script executions.
/// </summary>
public interface IScriptContext
{
    Result<IScriptExecutor> CreateExecutor(string command);
}
