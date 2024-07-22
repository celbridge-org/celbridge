using Celbridge.Commands;
using Celbridge.Scripting;
using System.Reflection;

namespace Celbridge.Scripting.Services;

public class ScriptingService : IScriptingService
{
    private List<string> _contextSetupCommands = new();

    public ScriptingService()
    {
        // Add context setup commands to bind all static methods defined on classes that
        // implement IExecutableCommand.
        var bindResult = BindExecutableCommands();
        if (bindResult.IsFailure)
        {
            throw new InvalidOperationException(bindResult.Error);
        }
    }

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

    private Result BindExecutableCommands()
    {
        //
        // Find all classes that inherit from IExecutableCommand
        //
        var commandTypes = new List<Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var types = assembly.GetTypes()
                                .Where(t => typeof(IExecutableCommand).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                                .ToList();
            commandTypes.AddRange(types);
        }
        commandTypes.Sort((a, b) => a.Name.CompareTo(b.Name));

        //
        // Find all assemblies that contain classes that inherit from CommandBase
        //
        var commandAssemblies = new List<Assembly>();
        foreach (var type in commandTypes)
        {
            if (type != null)
            {
                commandAssemblies.AddDistinct(type.Assembly);
            }
        }
        commandAssemblies.Sort((a, b) => a.GetName().Name!.CompareTo(b.GetName().Name!));

        //
        // Emit script to reference each assembly
        //
        foreach (var assembly in commandAssemblies)
        {
            var assemblyName = assembly.GetName().Name;
            var script = $"#r \"{assemblyName}\"";
            AddContextSetupCommand(script);
        }

        //
        // Emit script to bind the static methods in each command class
        //
        foreach (var commandType in commandTypes)
        {
            var typeName = commandType.FullName;
            var script = $"using static {typeName};";
            AddContextSetupCommand(script);
        }

        return Result.Ok();
    }
}
