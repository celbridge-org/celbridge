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
    Result<IDocumentEditorFactory> GetFactory(ResourceKey fileResource);

    /// <summary>
    /// Checks if any registered factory can handle the specified extension.
    /// </summary>
    bool IsExtensionSupported(string fileExtension);

    /// <summary>
    /// Checks if any registered factory is bound to the specified exact filename.
    /// Filename-only registrations (e.g. "package.cel") drive matching distinct
    /// from extension lookups.
    /// </summary>
    bool IsFilenameSupported(string fileName);

    /// <summary>
    /// Gets all registered factories.
    /// </summary>
    IReadOnlyList<IDocumentEditorFactory> GetAllFactories();

    /// <summary>
    /// Gets all factories indexed under the specified extension, sorted by
    /// priority. Direct bucket lookup; does not walk the multi-part suffix
    /// chain or apply CanHandleResource. Returns an empty list when no
    /// factory is registered for the extension.
    /// </summary>
    IReadOnlyList<IDocumentEditorFactory> GetFactoriesForExtension(string fileExtension);

    /// <summary>
    /// Gets every factory that can handle the given file, sorted by priority
    /// (most specialized first), deduplicated by editor id and filtered by
    /// CanHandleResource. Uses the same matching rules as GetFactory.
    /// </summary>
    IReadOnlyList<IDocumentEditorFactory> GetFactoriesForResource(ResourceKey fileResource);

    /// <summary>
    /// Returns the factories a user could reasonably pick from an "Open with..."
    /// dialog: non-placeholder factories that claim the file, plus the code
    /// editor appended as a "view as text" option for text-shaped files.
    /// </summary>
    IReadOnlyList<IDocumentEditorFactory> GetUserPickableFactoriesForResource(ResourceKey fileResource);

    /// <summary>
    /// Gets a factory by its editor ID.
    /// </summary>
    Result<IDocumentEditorFactory> GetFactoryById(DocumentEditorId documentEditorId);

    /// <summary>
    /// Gets the editor language identifier for the specified file extension.
    /// Queries registered factories in priority order and returns the first non-null result.
    /// Returns null if no factory provides a language mapping for the extension.
    /// </summary>
    string? GetLanguageForExtension(string fileExtension);

    /// <summary>
    /// Gets all file extensions supported by registered factories.
    /// </summary>
    IReadOnlyList<string> GetAllSupportedExtensions();
}
