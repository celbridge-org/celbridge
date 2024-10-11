using Celbridge.Foundation;

namespace Celbridge.ExtensionAPI;

/// <summary>
/// Provides common implementation code for managing extensions.
/// </summary>  
public abstract class ExtensionBase
{
    /// <summary>  
    /// Gets the context in which the extension operates.  
    /// </summary> 
    private IExtensionContext? _context;
    public IExtensionContext Context => _context!;

    /// <summary>  
    /// Initializes the extension with the provided context.  
    /// </summary>  
    public Result Load(IExtensionContext context)
    {
        _context = context;
        return OnLoadExtension();
    }

    /// <summary>  
    /// Uninitializes a previously initialized extension.  
    /// </summary>  
    public Result Unload()
    {
        return OnUnloadExtension();
    }

    /// <summary>  
    /// Called when the extension is uninitialized.  
    /// Override this method in your extension to handle extension shutdown.
    /// </summary>  
    public abstract Result OnUnloadExtension();

    /// <summary>  
    /// Called when the extension is initialized.  
    /// Override this method in your extension to handle extension setup.
    /// </summary>  
    public abstract Result OnLoadExtension();
}
