using Celbridge.Core;
using Celbridge.Extensions;
using CommunityToolkit.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;

namespace Celbridge.Services;

/// <summary>
/// A helper class that to load the extension assemblies for the current session.
/// Uses an AssemblyLoadContext to enabling dynamic loading and unloading of extension assemblies.
/// </summary>
public class ExtensionLoader : IDisposable
{
    private AssemblyLoadContext? _loadContext;
    private bool _disposed = false; // To detect redundant calls

    public Dictionary<string, IExtension> LoadedExtensions { get; } = new();

    public ExtensionLoader()
    {
        _loadContext = new AssemblyLoadContext("Celbridge.Extensions", isCollectible: true);
    }

    public Result<IExtension> LoadExtension(string assemblyName)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException("This ExtensionLoader has been disposed and cannot load assemblies.");
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
                // when you query it while debugging. To workaround this, you can iterate over the list of extension objects and get the assembly for each type.
                assembly = _loadContext.LoadFromAssemblyName(new AssemblyName(assemblyName));
            }

            //
            // Acquire the extension details from the loaded assembly
            //

            if (assembly is null)
            {
                return Result<IExtension>.Fail($"Failed to load extension assembly '{assemblyName}'. Unable to locate an assembly with this name.");
            }

            var extensionResult = AcquireExtensionInstance(assembly);
            if (extensionResult.IsFailure)
            {
                var errorResult = Result<IExtension>.Fail("Failed to load extension assembly. Unable to acquire extension instance from loaded assembly.") as Result;
                errorResult.MergeErrors(extensionResult);
                return (errorResult as Result<IExtension>)!;
            }
            var extension = extensionResult.Value;

            LoadedExtensions[assemblyName] = extension;
            return Result<IExtension>.Ok(extension);
        }
        catch (Exception ex)
        {
            return Result<IExtension>.Fail($"An exception occurred attempting to load extension in assembly `{assemblyName}`. {ex}");
        }
    }

    private Result<IExtension> AcquireExtensionInstance(Assembly extensionAssembly)
    {
        Guard.IsNotNull(extensionAssembly);

        // Find all types that implement the IExtension interface
        var extensionTypes = extensionAssembly.GetTypes()
            .Where(t => typeof(IExtension).IsAssignableFrom(t) &&
                   !t.IsInterface &&
                   !t.IsAbstract);

        if (extensionTypes.Count() == 0)
        {
            return Result<IExtension>.Fail($"Failed to acquire extension instance because assembly '{extensionAssembly.GetName()}' does not contain a type that implements IExtension.");
        }

        if (extensionTypes.Count() > 1)
        {
            return Result<IExtension>.Fail($"Failed to acquire extension instance because assembly '{extensionAssembly.GetName()}' contains multiple types that implement IExtension.");
        }

        var extensionType = extensionTypes.First();
        try
        {
            // Create an instance of the class
            var instance = Activator.CreateInstance(extensionType) as IExtension;
            Guard.IsNotNull(instance);

            return Result<IExtension>.Ok(instance);
        }
        catch (Exception ex)
        {
            return Result<IExtension>.Fail($"Error initializing extension {extensionType.Name}: {ex.Message}");
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

    public Result InitializeExtensions()
    {
        foreach (var kv in LoadedExtensions)
        {
            var extensionName = kv.Key;
            var extension = kv.Value;

            var initializeResult = extension.Initialize();
            if (initializeResult.IsFailure)
            {
                return Result.Fail($"Failed to load extension '{extensionName}'");
            }
        }

        return Result.Ok();
    }

    ~ExtensionLoader()
    {
        Dispose(false);
    }
}
