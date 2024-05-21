using Celbridge.BaseLibrary.Scripting;
using CommunityToolkit.Diagnostics;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.CSharp;
using Microsoft.DotNet.Interactive.Events;

namespace Celbridge.Scripting.DotNetInteractive;

public class DotNetInteractiveExecutionContext : ScriptExecutionContext
{
    public DotNetInteractiveExecutionContext(IScriptContext scriptContext, string command)
        : base(scriptContext, command)
    { }

    public override async Task<Result> ExecuteAsync()
    {
        if (string.IsNullOrEmpty(Command))
        {
            return Result.Fail("Command cannot be null or empty.");
        }

        if (Status != ExecutionStatus.NotStarted)
        {
            return Result.Fail($"Failed to execute ScriptExecutionContext because it is in the '{Status}' status.");
        }

        // Execute the command using .Net Interactive

        var dotNetInteractiveContext = ScriptContext as DotNetInteractiveContext;
        Guard.IsNotNull(dotNetInteractiveContext);
        CSharpKernel kernel = dotNetInteractiveContext.Kernel;

        var result = await kernel.SendAsync(new SubmitCode(Command));

        if (result.Events.OfType<CommandFailed>().Any())
        {
            foreach (var error in result.Events.OfType<CommandFailed>())
            {
                WriteError(error.Message);
            }
            Status = ExecutionStatus.Error;

            return Result.Ok();
        }

        if (result.Events.OfType<StandardOutputValueProduced>().Any())
        {
            foreach (var output in result.Events.OfType<StandardOutputValueProduced>())
            {
                foreach (var formattedValue in output.FormattedValues)
                {
                    if (formattedValue is null)
                    {
                        continue;
                    }

                    var outputText = formattedValue.Value;
                    if (!string.IsNullOrEmpty(outputText))
                    {                    
                        WriteOutput(outputText.TrimEnd());
                    }
                }
            }
        }

        if (result.Events.OfType<ReturnValueProduced>().Any())
        {
            var returnValue = result.Events.OfType<ReturnValueProduced>().First().Value;
            if (returnValue is not null)
            {
                var returnText = returnValue.ToString();
                if (!string.IsNullOrEmpty(returnText))
                {
                    WriteOutput(returnText);
                }
            }
        }
        
        Status = ExecutionStatus.Finished;

        return Result.Ok();
    }
}