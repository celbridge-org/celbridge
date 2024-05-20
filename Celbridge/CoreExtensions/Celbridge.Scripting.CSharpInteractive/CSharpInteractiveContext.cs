using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting.CSharpInteractive;

public class CSharpInteractiveContext : IScriptContext
{
    public Result<ScriptExecutionContext> CreateExecutionContext(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return Result<ScriptExecutionContext>.Fail("Command cannot be null or empty.");
        }

        var scriptExecutionContext = new CSharpInteractiveExecutionContext(command);

        return Result<ScriptExecutionContext>.Ok(scriptExecutionContext);
    }

    Result<ScriptExecutionContext> IScriptContext.CreateExecutionContext(string command)
    {
        throw new NotImplementedException();
    }
}
