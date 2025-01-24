using System.Reflection;
using System.Runtime.Loader;

namespace Celbridge.Modules.Services;

/// <summary>
/// A helper class that loads the module assemblies for the application lifetime.
/// Uses an AssemblyLoadContext to enabling dynamic loading and unloading of module assemblies.
/// </summary>
public class ModuleLoader : IDisposable
{
    private AssemblyLoadContext? _loadContext;
    private bool _disposed = false; // To detect redundant calls

    public Dictionary<string, IModule> LoadedModules { get; } = new();

    public ModuleLoader()
    {
        _loadContext = new AssemblyLoadContext("Celbridge.Modules", isCollectible: true);
    }

    public Result<IModule> LoadModules(string assemblyName)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException("This ModuleLoader has been disposed and cannot load assemblies.");
        }

        Guard.IsNotNull(_loadContext);

        try
        {
            //
            // Acquire the assembly by name
            //

            // First check if the assembly is already loaded
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var assembly = assemblies.FirstOrDefault(a =>
            {
                var name = a.GetName().Name;
                if (string.IsNullOrEmpty(name))
                {
                    return false;
                }
                return name.Equals(assemblyName);
            });

            if (assembly is null)
            {
                // Assembly is not already loaded, so load it now.
                // Note: AssemblyLoadContext.Assemblies should contain a list of the loaded assemblies, but I think the Visual Studio debugger
                // is causing the default context to load the assembly rather than our custom context. This means that the Assemblies list property is empty
                // when you query it while debugging. To workaround this, you can iterate over the list of loaded modules and get the assembly for each type.
                assembly = _loadContext.LoadFromAssemblyName(new AssemblyName(assemblyName));
            }

            //
            // Acquire the module details from the loaded assembly
            //

            if (assembly is null)
            {
                return Result<IModule>.Fail($"Failed to load module assembly: '{assemblyName}'");
            }

            var acquireResult = AcquireModuleInstance(assembly);
            if (acquireResult.IsFailure)
            {
                return Result<IModule>.Fail("Failed to acquire module instance from loaded assembly.")
                    .WithErrors(acquireResult);
            }
            var module = acquireResult.Value;

            LoadedModules[assemblyName] = module;
            return Result<IModule>.Ok(module);
        }
        catch (Exception ex)
        {
            return Result<IModule>.Fail($"An exception occurred attempting to load module in assembly `{assemblyName}`. {ex}");
        }
    }

    private Result<IModule> AcquireModuleInstance(Assembly moduleAssembly)
    {
        Guard.IsNotNull(moduleAssembly);

        // Find all types that implement the IModule interface
        var moduleTypes = moduleAssembly.GetTypes()
            .Where(t => typeof(IModule).IsAssignableFrom(t) &&
                   !t.IsInterface &&
                   !t.IsAbstract);

        if (moduleTypes.Count() == 0)
        {
            return Result<IModule>.Fail($"Failed to acquire module instance because assembly '{moduleAssembly.GetName()}' does not contain a type that implements IModule.");
        }

        if (moduleTypes.Count() > 1)
        {
            return Result<IModule>.Fail($"Failed to acquire module instance because assembly '{moduleAssembly.GetName()}' contains multiple types that implement IModule.");
        }

        var moduleType = moduleTypes.First();
        try
        {
            // Create an instance of the class
            var instance = Activator.CreateInstance(moduleType) as IModule;
            Guard.IsNotNull(instance);

            return Result<IModule>.Ok(instance);
        }
        catch (Exception ex)
        {
            return Result<IModule>.Fail($"An exception occurred when initializing module {moduleType.Name}")
                .WithException(ex);
        }
    }

    // Public implementation of Dispose pattern callable by consumers.
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    // Protected implementation of Dispose pattern.
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            Guard.IsNotNull(_loadContext);

            // Dispose managed state (managed objects).
            _loadContext.Unload(); // Unload the AssemblyLoadContext
            _loadContext = null; // Allow the AssemblyLoadContext to be garbage collected
        }

        // Free any unmanaged resources (if any) here.

        _disposed = true;

        // Force a garbage collection to clean up any unloaded assemblies
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public Result InitializeModules()
    {
        foreach (var kv in LoadedModules)
        {
            var moduleName = kv.Key;
            var module = kv.Value;

            var initializeResult = module.Initialize();
            if (initializeResult.IsFailure)
            {
                return Result.Fail($"Failed to initialize module: '{moduleName}'");
            }
        }

        return Result.Ok();
    }

    ~ModuleLoader()
    {
        Dispose(false);
    }
}
