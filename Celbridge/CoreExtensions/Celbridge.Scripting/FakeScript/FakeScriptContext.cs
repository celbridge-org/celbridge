using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting.FakeScript;

public class FakeScriptContext : IScriptContext
{
    public Result<ScriptExecutionContext> CreateExecutionContext(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return Result<ScriptExecutionContext>.Fail("Command cannot be null or empty.");
        }

        var scriptExecutionContext = new FakeScriptExecutionContext(this, command);

        return Result<ScriptExecutionContext>.Ok(scriptExecutionContext);
    }
}
