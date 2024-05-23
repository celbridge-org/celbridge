using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting;

public class ScriptingService : IScriptingService
{
    private List<string> _contextSetupCommands = new();

    public async Task<IScriptContext> CreateScriptContext()
    {
        var context = new DotNetInteractiveContext();

        foreach (var command in _contextSetupCommands)
        {
            var createResult = context.CreateExecutor(command);
            if (createResult.IsFailure)
            {
                throw new Exception($"Failed to create script executor: {createResult.Error}");
            }

            var executor = createResult.Value;
            var executeResult = await executor.ExecuteAsync();
            if (executeResult.IsFailure)
            {
                throw new Exception($"Failed to execute script command: {executeResult.Error}");
            }
        }

        return context;
    }

    public void AddContextSetupCommand(string command)
    {
        _contextSetupCommands.Add(command);
    }
}
