namespace Celbridge.Resources;

/// <summary>
/// Dumps the contents of the resource registry to a file for debugging purposes.
/// </summary>
public interface IResourceRegistryDumper
{
    /// <summary>
    /// Create the dump file in the specified folder.
    /// </summary>
    Result Initialize(string logFolderPath);
}
