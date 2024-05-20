using Celbridge.Scripting;

namespace Celbridge.BaseLibrary.Scripting;

public interface IScriptContext
{
    Result<IScriptExecutionContext> Execute(string command);
}
