namespace Celbridge.Documents;

/// <summary>
/// Property path strings for document component properties
/// Paths use JSON Pointer (RFC 6901) syntax.
/// </summary>
public static class DocumentConstants
{
    /// <summary>
    /// Editor mode property path.
    /// </summary>
    public const string EditorModeProperty = "/editorMode";

    /// <summary>
    /// Editor enabled property path.
    /// </summary>
    public const string EditorEnabledProperty = "/editorEnabled";

    /// <summary>
    /// Editor and preview enabled property path.
    /// </summary>
    public const string EditorAndPreviewEnabledProperty = "/editorAndPreviewEnabled";

    /// <summary>
    /// Editor and preview enabled property path.
    /// </summary>
    public const string PreviewEnabledProperty = "/previewEnabled";
}
