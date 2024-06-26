using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Scripting;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Celbridge.ScriptUtils;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {}

    public Result Initialize()
    {
        //
        // Bind to the utilities defined in this assembly
        //
        var scriptingService = ServiceLocator.ServiceProvider.GetRequiredService<IScriptingService>();
        scriptingService.AddContextSetupCommand("#r \"Celbridge.ScriptUtils\"");
        scriptingService.AddContextSetupCommand("using static Celbridge.ScriptUtils.Services.CommonUtils;");

        //
        // Bind to static methods defined on any class that inherits from CommandBase
        //
        try
        {
            BindStaticCommandMethods();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to initialize script support. {ex.Message}");
        }

        return Result.Ok();
    }

    private void BindStaticCommandMethods()
    {
        var scriptingService = ServiceLocator.ServiceProvider.GetRequiredService<IScriptingService>();

        //
        // Find all classes that inherit from CommandBase
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
            scriptingService.AddContextSetupCommand(script);
        }

        //
        // Emit script to bind the static methods in each command class
        //
        foreach (var commandType in commandTypes)
        {
            var typeName = commandType.FullName;
            var script = $"using static {typeName};";
            scriptingService.AddContextSetupCommand(script);
        }
    }

}
