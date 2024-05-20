using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting.EchoScript;

public class FakeScriptContext : IScriptContext
{
    public Result<ScriptExecutionContext> CreateExecutionContext(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return Result<ScriptExecutionContext>.Fail("Command cannot be null or empty.");
        }

        var scriptExecutionContext = new FakeScriptExecutionContext(command);

        return Result<ScriptExecutionContext>.Ok(scriptExecutionContext);
    }
}
