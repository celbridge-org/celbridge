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
    /// Sets the validated editor-associations map of file extension to editor id.
    /// </summary>
    void SetEditorAssociations(IReadOnlyDictionary<string, string> editorAssociations);

    /// <summary>
    /// Gets the factory named by the editor-associations entry whose extension is the longest
    /// matching suffix of the file name. Fails when no entry matches or the named editor cannot
    /// handle the resource.
    /// </summary>
    Result<IDocumentEditorFactory> GetAssociatedEditorFactory(ResourceKey fileResource);

    /// <summary>
    /// Gets the factory for the specified file resource.
    /// Returns the first factory in resolution order that can handle the resource.
    /// </summary>
    Result<IDocumentEditorFactory> GetFactory(ResourceKey fileResource);

    /// <summary>
    /// Checks if any registered factory can handle the specified extension.
    /// </summary>
    bool IsExtensionSupported(string fileExtension);

    /// <summary>
    /// Gets all registered factories.
    /// </summary>
    IReadOnlyList<IDocumentEditorFactory> GetAllFactories();

    /// <summary>
    /// Gets all factories indexed under the specified extension, in resolution order, without
    /// walking the multi-part suffix chain or applying CanHandleResource. Returns an empty
    /// list when no factory is registered for the extension.
    /// </summary>
    IReadOnlyList<IDocumentEditorFactory> GetFactoriesForExtension(string fileExtension);

    /// <summary>
    /// Gets every factory that can handle the given file, in resolution order, deduplicated by
    /// editor id and filtered by CanHandleResource. More specific matches win first: a longer
    /// extension suffix outranks a shorter one, and within one suffix declared instances come in
    /// declaration order, then built-ins in host order.
    /// </summary>
    IReadOnlyList<IDocumentEditorFactory> GetFactoriesForResource(ResourceKey fileResource);

    /// <summary>
    /// Returns the factories a user could reasonably pick from an "Open with..."
    /// dialog: non-placeholder factories that claim the file, plus the code
    /// editor appended as a "view as text" option for text-shaped files.
    /// </summary>
    IReadOnlyList<IDocumentEditorFactory> GetUserPickableFactoriesForResource(ResourceKey fileResource);

    /// <summary>
    /// The user-pickable factories for a bare file extension, in resolution order. Resolves the same
    /// way an actual file of that extension would, for the Project Settings File Types page. Empty when
    /// nothing claims the extension and it is not text-shaped.
    /// </summary>
    IReadOnlyList<IDocumentEditorFactory> GetUserPickableFactoriesForExtension(string fileExtension);

    /// <summary>
    /// Gets a factory by its editor ID.
    /// </summary>
    Result<IDocumentEditorFactory> GetFactoryById(EditorInstanceId editorId);

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
