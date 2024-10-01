using Celbridge.Commands;
using Celbridge.Console.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using System.Reflection;
using System.Text;

using Path = System.IO.Path;

namespace Celbridge.Scripting.Services;

public class ScriptingService : IScriptingService, IDisposable
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private List<string> _contextSetupCommands = new();
    private List<string> _methodDescriptions = new();
    private bool _initialized = false;

    public ScriptingService(
        IStringLocalizer stringLocalizer, 
        IWorkspaceWrapper workspaceWrapper)
    {
        _stringLocalizer = stringLocalizer;
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<IScriptContext> CreateScriptContext()
    {
        if (!_initialized)
        {
            BindScriptingMethods();
            _initialized = true;
        }

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

    private Result BindScriptingMethods()
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

        //
        // Populate the method descriptions for the Help() command
        //
        var methodPrinter = new MethodPrinter();
        foreach (var commandType in commandTypes)
        {
            var methodSignatures = methodPrinter.GetMethodSignatures(commandType);
            _methodDescriptions.AddRange(methodSignatures);
        }

        //
        // Set the current directory to the project folder so that ResourceKeys are valid relative paths
        //
        var projectFolderPath = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry.ProjectFolderPath;
        projectFolderPath = Path.GetFullPath(projectFolderPath);
        projectFolderPath = projectFolderPath.Replace(@"\", @"\\");

        if (Path.Exists(projectFolderPath))
        {
            var currentDirScript = $"System.IO.Directory.SetCurrentDirectory(\"{projectFolderPath}\");";
            AddContextSetupCommand(currentDirScript);
        }

        return Result.Ok();
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed objects here
            }

            _disposed = true;
        }
    }

    ~ScriptingService()
    {
        Dispose(false);
    }
}
