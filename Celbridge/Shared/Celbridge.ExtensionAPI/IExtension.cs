using Celbridge.Foundation;

namespace Celbridge.ExtensionAPI;

/// <summary>  
/// Interface for a Celbridge extension.  
/// </summary>  
public interface IExtension
{
    /// <summary>  
    /// Gets the context in which the extension operates.  
    /// </summary>  
    public IExtensionContext Context { get; }

    /// <summary>  
    /// Initializes the extension with the provided context.  
    /// </summary>  
    public Result Initialize(IExtensionContext context);

    /// <summary>  
    /// Unloads the extension.  
    /// </summary>  
    Result Unload();
}
