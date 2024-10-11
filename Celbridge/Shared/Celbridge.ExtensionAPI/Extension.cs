namespace Celbridge.ExtensionAPI;

/// <summary>
/// Provides common implementation code for managing extensions.
/// </summary>  
public abstract class Extension
{
    /// <summary>  
    /// Gets the context in which the extension operates.  
    /// </summary> 
    private IExtensionContext? _context;
    public IExtensionContext Context => _context!;

    /// <summary>  
    /// Loads the extension and initializes the context.  
    /// </summary>  
    public Result Load(IExtensionContext context)
    {
        _context = context;
        return OnLoadExtension();
    }

    /// <summary>  
    /// Unloads a previously loaded extension.
    /// </summary>  
    public Result Unload()
    {
        return OnUnloadExtension();
    }

    /// <summary>  
    /// Called when the extension is loaded.  
    /// Override this method in your extension to handle extension shutdown.
    /// </summary>  
    public abstract Result OnUnloadExtension();

    /// <summary>  
    /// Called when the extension is unloaded.  
    /// Override this method in your extension to handle extension setup.
    /// </summary>  
    public abstract Result OnLoadExtension();
}
