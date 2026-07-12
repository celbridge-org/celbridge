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
    /// Extensions should be lowercase with leading dot. Multi-part forms (a name
    /// ending in more than one dotted segment) are also accepted; the registry
    /// resolves longest match first when a file's name matches more than one
    /// registered suffix.
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Exact file names this factory handles (e.g., "package.toml"). Filename
    /// matches are tried before extension matches. Names are compared
    /// case-insensitively. Defaults to an empty list when the factory matches
    /// purely by extension.
    /// </summary>
    IReadOnlyList<string> SupportedFilenames { get; }

    /// <summary>
    /// Priority for conflict resolution when multiple factories support the same extension.
    /// Specialized editors take precedence over general-purpose editors.
    /// </summary>
    EditorPriority Priority { get; }

    /// <summary>
    /// True for factories that exist solely to reserve a filename or extension
    /// for a known non-document role (e.g. package.toml, *.celbridge,
    /// *.document.toml). Placeholders do not produce real document views and
    /// are hidden from user-facing pickers such as the "Open with..." menu.
    /// </summary>
    bool IsPlaceholder { get; }

    /// <summary>
    /// True for factories that produce utility documents: title-bar-launched editors
    /// backed by a fixed utils: resource rather than an extension claimed across the project.
    /// </summary>
    bool IsUtility { get; }

    /// <summary>
    /// Determines if this factory can handle the given file resource.
    /// </summary>
    bool CanHandleResource(ResourceKey fileResource);

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
