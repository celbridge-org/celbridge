using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting.DotNetInteractive;

public class DotNetInteractiveContext : IScriptContext
{
    public Result<ScriptExecutionContext> CreateExecutionContext(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return Result<ScriptExecutionContext>.Fail("Command cannot be null or empty.");
        }

        var scriptExecutionContext = new DotNetInteractiveExecutionContext(command);

        return Result<ScriptExecutionContext>.Ok(scriptExecutionContext);
    }
}
