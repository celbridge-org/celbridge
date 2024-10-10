namespace Celbridge.Extensions;

/// <summary>
/// Interface for managing the loading and unloading of extensions.
/// </summary>
public interface IExtensionService
{
    /// <summary>
    /// Loads the specified extensions.
    /// </summary>
    Result LoadExtensions(List<string> extensions);

    /// <summary>
    /// Unloads all currently loaded extensions.
    /// </summary>
    Result UnloadExtensions();
}
