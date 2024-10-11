namespace Celbridge.ExtensionAPI;

/// <summary>  
/// Manages all communication between the application and an extension instance.
/// </summary>  
public interface IExtensionContext
{
    /// <summary>
    /// Maps supported file extensions to text editor preview providers.
    /// </summary>
    Dictionary<string, IPreviewProvider> PreviewProviders { get; }
}
