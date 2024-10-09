namespace Celbridge.Extensions;

/// <summary>
/// Provides services for managing extensions.
/// </summary>
public interface IExtensionService
{
    /// <summary>
    /// Initializes all loaded extensions
    /// </summary>
    Result InitializeExtensions();
}
