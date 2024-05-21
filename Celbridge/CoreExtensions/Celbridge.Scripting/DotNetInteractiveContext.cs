using Celbridge.BaseLibrary.Scripting;
using Microsoft.DotNet.Interactive.CSharp;

namespace Celbridge.Scripting;

public class DotNetInteractiveContext : IScriptContext
{
    public CSharpKernel Kernel { get; } = new();

    public Result<IScriptExecutor> CreateExecutor(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return Result<IScriptExecutor>.Fail("Command text cannot be null or empty.");
        }

        var scriptExecutionContext = new DotNetInteractiveExecutor(this, command);

        return Result<IScriptExecutor>.Ok(scriptExecutionContext);
    }
}
