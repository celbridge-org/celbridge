using Celbridge.BaseLibrary.Scripting;
using Microsoft.DotNet.Interactive;

namespace Celbridge.Scripting.Services;

public class DotNetInteractiveContext : IScriptContext
{
    public CompositeKernel Kernel { get; init; } = KernelBuilder.CreateKernel("csharp");

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
