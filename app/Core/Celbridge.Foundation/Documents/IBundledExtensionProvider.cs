namespace Celbridge.Documents;

/// <summary>
/// Implemented by modules that provide bundled extension editors.
/// The returned path points to a directory containing an editor.json manifest.
/// </summary>
public interface IBundledExtensionProvider
{
    /// <summary>
    /// Gets the absolute path to the bundled extension directory.
    /// </summary>
    string GetExtensionDirectory();
}
