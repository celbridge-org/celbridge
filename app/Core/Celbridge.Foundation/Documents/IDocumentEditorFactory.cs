namespace Celbridge.Documents;

/// <summary>
/// Factory for creating document views for specific file extensions.
/// </summary>
public interface IDocumentEditorFactory
{
    /// <summary>
    /// The file extensions this factory handles (e.g., ".md", ".txt", ".cs").
    /// Extensions should be lowercase with leading dot.
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Priority for conflict resolution when multiple factories support the same extension.
    /// Higher values take precedence. Default factories should use 0.
    /// </summary>
    int Priority => 0;

    /// <summary>
    /// Determines if this factory can handle the given file resource.
    /// This allows for more sophisticated matching beyond just file extension
    /// (e.g., checking file content, size, or other criteria).
    /// </summary>
    bool CanHandle(ResourceKey fileResource, string filePath);

    /// <summary>
    /// Creates a document view for the specified file resource.
    /// </summary>
    Result<IDocumentView> CreateDocumentView(ResourceKey fileResource);

    /// <summary>
    /// Gets the editor language identifier for the specified file extension.
    /// Returns null if this factory doesn't provide language mapping for the extension.
    /// </summary>
    string? GetLanguageForExtension(string extension) => null;
}
