using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting.Services;

public class ScriptingService : IScriptingService
{
    private List<string> _contextSetupCommands = new();

    public async Task<IScriptContext> CreateScriptContext()
    {
        var context = new DotNetInteractiveContext();

        foreach (var command in _contextSetupCommands)
        {
            // Create and run an executor for each registered setup command
            var createResult = context.CreateExecutor(command);
            if (createResult.IsFailure)
            {
                throw new InvalidOperationException($"Failed to create script executor: {createResult.Error}");
            }

            var executor = createResult.Value;

            executor.OnError += (error) =>
            {
                throw new InvalidOperationException($"Error executing context setup command: {error}");
            };

            var executeResult = await executor.ExecuteAsync();
            if (executeResult.IsFailure)
            {
                throw new InvalidOperationException($"Failed to execute script command: {executeResult.Error}");
            }
        }

        return context;
    }

    public void AddContextSetupCommand(string command)
    {
        _contextSetupCommands.Add(command);
    }
}
