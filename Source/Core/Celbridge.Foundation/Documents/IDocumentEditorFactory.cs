namespace Celbridge.Documents;

/// <summary>
/// Editor priority for conflict resolution when multiple editors support the same file type.
/// When multiple factories support the same extension, lower priority values win.
/// </summary>
public enum EditorPriority
{
    /// <summary>
    /// Purpose-built editor for a specific file type (e.g., Markdown editor, Spreadsheet editor).
    /// </summary>
    Specialized,

    /// <summary>
    /// General-purpose editor that handles many file types (e.g., code/text editor).
    /// </summary>
    General
}

/// <summary>
/// Factory for creating document views for specific file extensions.
/// </summary>
public interface IDocumentEditorFactory
{
    /// <summary>
    /// Stable identifier for this editor.
    /// </summary>
    DocumentEditorId EditorId { get; }

    /// <summary>
    /// Localized display name for this editor, shown in menus and tooltips.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// The file extensions this factory handles (e.g., ".md", ".txt", ".cs").
    /// Extensions should be lowercase with leading dot.
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Priority for conflict resolution when multiple factories support the same extension.
    /// Specialized editors take precedence over general-purpose editors.
    /// </summary>
    EditorPriority Priority { get; }

    /// <summary>
    /// Determines if this factory can handle the given file resource.
    /// </summary>
    bool CanHandleResource(ResourceKey fileResource, string filePath);

    /// <summary>
    /// Creates a document view for the specified file resource.
    /// </summary>
    Result<IDocumentView> CreateDocumentView(ResourceKey fileResource);

    /// <summary>
    /// Gets the editor language identifier for the specified file extension.
    /// Returns null if this factory doesn't provide language mapping for the extension.
    /// </summary>
    string? GetLanguageForExtension(string extension);
}
