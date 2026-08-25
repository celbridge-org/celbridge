namespace Celbridge.Documents;

/// <summary>
/// Factory for creating document views for specific file extensions.
/// </summary>
public interface IDocumentEditorFactory
{
    /// <summary>
    /// Stable identifier for this editor.
    /// </summary>
    EditorId EditorId { get; }

    /// <summary>
    /// Localized display name for this editor, shown in menus and tooltips.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// The file extensions this factory handles, lowercase with a leading dot (e.g. ".md", ".txt", ".cs").
    /// Multi-part forms, a name ending in more than one dotted segment, are also accepted.
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Exact file names this factory handles (e.g. "package.toml"), compared case-insensitively.
    /// Empty when the factory matches purely by extension.
    /// </summary>
    IReadOnlyList<string> SupportedFilenames { get; }

    /// <summary>
    /// True for factories that exist solely to reserve a filename or extension
    /// for a known non-document role (e.g. package.toml, *.celbridge,
    /// *.editor.toml). Placeholders do not produce real document views and
    /// are hidden from user-facing pickers such as the "Open with..." menu.
    /// </summary>
    bool IsPlaceholder { get; }

    /// <summary>
    /// The title a document tab shows instead of the file name, or empty to use the file name. Set by an
    /// editor whose file is fixed and whose name adds nothing to a tab strip; a tab titled this way is
    /// left out of filename disambiguation.
    /// </summary>
    string DocumentTabTitle { get; }

    /// <summary>
    /// True when the extensions and filenames this factory claims carry a role the application depends
    /// on, so they are not file types a user may point at a different editor. Placeholders reserve by
    /// definition; an editor that both reserves a type and opens it sets this itself.
    /// </summary>
    bool ReservesFileType { get; }

    /// <summary>
    /// True for factories that produce utility documents: Utility Panel surfaces backed by a fixed utils:
    /// resource rather than an extension claimed across the project.
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
