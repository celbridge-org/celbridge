using Celbridge.Scripting;

namespace Celbridge.BaseLibrary.Scripting;

public interface IScriptContext
{
    Result<ScriptExecutionContext> CreateExecutionContext(string command);
}
