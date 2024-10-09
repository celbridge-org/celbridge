using Celbridge.Foundation;
using CommunityToolkit.Diagnostics;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.Events;

namespace Celbridge.Scripting.Services;

public class DotNetInteractiveExecutor : IScriptExecutor
{
    public IScriptContext ScriptContext { get; init; }

    public string Command { get; init; }

    public ExecutionStatus Status { get; protected set; } = ExecutionStatus.NotStarted;

    public event Action<string>? OnOutput;

    public event Action<string>? OnError;

    public DotNetInteractiveExecutor(IScriptContext scriptContext, string command)
    {
        ScriptContext = scriptContext;
        Command = command;
    }

    protected virtual void WriteOutput(string output)
    {
        OnOutput?.Invoke(output);
    }

    protected virtual void WriteError(string error)
    {
        OnError?.Invoke(error);
    }

    public virtual async Task<Result> ExecuteAsync()
    {
        if (string.IsNullOrEmpty(Command))
        {
            return Result.Fail("Command cannot be null or empty.");
        }

        if (Status != ExecutionStatus.NotStarted)
        {
            return Result.Fail($"Failed to execute ScriptExecutionContext because it is in the '{Status}' status.");
        }

        await ExecuteCommand();

        return Result.Ok();
    }

    protected virtual async Task ExecuteCommand()
    {
        var dotNetInteractiveContext = ScriptContext as DotNetInteractiveContext;
        Guard.IsNotNull(dotNetInteractiveContext);
        var kernel = dotNetInteractiveContext.Kernel;

        try
        {
            Status = ExecutionStatus.InProgress;

            var result = await kernel.SendAsync(new SubmitCode(Command));

            if (result.Events.OfType<CommandFailed>().Any())
            {
                foreach (var error in result.Events.OfType<CommandFailed>())
                {
                    WriteError(error.Message);
                }

                Status = ExecutionStatus.Error;
                return;
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
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            Status = ExecutionStatus.Error;
        }

        Status = ExecutionStatus.Finished;
    }
}