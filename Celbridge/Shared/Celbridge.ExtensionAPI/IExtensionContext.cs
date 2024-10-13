namespace Celbridge.ExtensionAPI;

/// <summary>  
/// Manages all communication between the application and an extension instance.
/// </summary>  
public interface IExtensionContext
{
    /// <summary>
    /// Adds a preview provider that generates a HTML preview for text editor documents.
    /// </summary>
    Result AddPreviewProvider(PreviewProvider previewProvider);
}
