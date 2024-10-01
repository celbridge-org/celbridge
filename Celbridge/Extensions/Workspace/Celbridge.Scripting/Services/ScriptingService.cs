using Celbridge.Commands;
using Celbridge.Console.Services;
using Microsoft.Extensions.Localization;
using System.Reflection;
using System.Text;

namespace Celbridge.Scripting.Services;

public class ScriptingService : IScriptingService
{
    private readonly IStringLocalizer _stringLocalizer;

    private List<string> _contextSetupCommands = new();

    public List<string> _methodDescriptions = new();

    public ScriptingService(IStringLocalizer stringLocalizer)
    {
        _stringLocalizer = stringLocalizer;

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

    public string GetHelpText(string searchTerm)
    {
        var sb = new StringBuilder();
        foreach (var methodDescription in _methodDescriptions)
        {
            if (methodDescription.Contains(searchTerm, StringComparison.InvariantCultureIgnoreCase))
            {
                sb.AppendLine(methodDescription);
            }

        }

        var helpText = sb.ToString();
        if (string.IsNullOrEmpty(helpText))
        {
            // Inform the user that no matching method signatures were found
            helpText = _stringLocalizer.GetString("ScriptingService_NoMatchesFound", searchTerm);
        }

        return helpText;
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

        var methodPrinter = new MethodPrinter();
        foreach (var commandType in commandTypes)
        {
            var methodSignatures = methodPrinter.GetMethodSignatures(commandType);
            _methodDescriptions.AddRange(methodSignatures);
        }

        return Result.Ok();
    }

    public List<string> GetMethodSignatures(Type type)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);

        var methodSignatures = new List<string>();
        foreach (var method in methods)
        {
            string methodName = method.Name;

            var parameters = method.GetParameters()
                                   .Select(p => $"{p.ParameterType.Name} {p.Name}")
                                   .ToArray();

            string methodSignature = $"{methodName}({string.Join(", ", parameters)})";
            methodSignatures.Add(methodSignature);
        }

        return methodSignatures;
    }
}
