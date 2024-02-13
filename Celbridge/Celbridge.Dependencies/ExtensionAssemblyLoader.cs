using System.Runtime.Loader;
using System.Reflection;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Dependencies;

/// <summary>
/// A helper class that to load the extension assemblies for the current session.
/// Uses an AssemblyLoadContext to enabling dynamic loading and unloading of extension assemblies.
/// </summary>
public class ExtensionAssemblyLoader : IDisposable
{
    private AssemblyLoadContext? _loadContext;
    private bool _disposed = false; // To detect redundant calls

    public Dictionary<string, Assembly> LoadedAssemblies { get; } = new();

    public ExtensionAssemblyLoader()
    {
        _loadContext = new AssemblyLoadContext("Celbridge.Extensions", isCollectible: true);
    }

    public Assembly LoadAssembly(string assemblyName)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException("This ExtensionLoader has been disposed and cannot load assemblies.");
        }

        Guard.IsNotNull(_loadContext);

        var loadedAssembly = _loadContext.LoadFromAssemblyName(new AssemblyName(assemblyName));

        // AssemblyLoadContext.Assemblies should do this caching for us, but I think the Visual Studio debugger
        // is causing the default context to load the assembly rather than our custom context, so the 
        // Assemblies list is empty when I query it. To workaround this issue, I maintain our own cache in LoadedAssemblies.
        LoadedAssemblies[assemblyName] = loadedAssembly;

        return loadedAssembly;
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
            LoadedAssemblies.Clear();
            _loadContext.Unload(); // Unload the AssemblyLoadContext
            _loadContext = null; // Allow the AssemblyLoadContext to be garbage collected
        }

        // Free any unmanaged resources (if any) here.

        _disposed = true;

        // Force a garbage collection to clean up any unloaded assemblies
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    ~ExtensionAssemblyLoader()
    {
        Dispose(false);
    }
}
