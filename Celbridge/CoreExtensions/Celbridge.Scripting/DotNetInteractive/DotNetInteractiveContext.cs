using Celbridge.BaseLibrary.Scripting;
using Microsoft.DotNet.Interactive.CSharp;

namespace Celbridge.Scripting.DotNetInteractive;

public class DotNetInteractiveContext : IScriptContext
{
    public CSharpKernel Kernel { get; } = new();

    public Result<ScriptExecutionContext> CreateExecutionContext(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return Result<ScriptExecutionContext>.Fail("Command cannot be null or empty.");
        }

        var scriptExecutionContext = new DotNetInteractiveExecutionContext(this, command);

        return Result<ScriptExecutionContext>.Ok(scriptExecutionContext);
    }
}
