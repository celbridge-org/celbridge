namespace Celbridge.Documents;

/// <summary>
/// Registry for document editor factories.
/// </summary>
public interface IDocumentEditorRegistry
{
    /// <summary>
    /// Registers a document editor factory.
    /// </summary>
    Result RegisterFactory(IDocumentEditorFactory factory);

    /// <summary>
    /// Gets the factory for the specified file resource.
    /// Returns the highest priority factory that can handle the resource.
    /// </summary>
    Result<IDocumentEditorFactory> GetFactory(ResourceKey fileResource, string filePath);

    /// <summary>
    /// Checks if any registered factory can handle the specified extension.
    /// </summary>
    bool IsExtensionSupported(string fileExtension);

    /// <summary>
    /// Gets all registered factories.
    /// </summary>
    IReadOnlyList<IDocumentEditorFactory> GetAllFactories();
}
