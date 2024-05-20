using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting.EchoScript;

public class EchoScriptContext : IScriptContext
{
    public Result<IScriptExecutionContext> Execute(string command)
    {
        return Result<IScriptExecutionContext>.Fail("Not implemented");
    }
}
