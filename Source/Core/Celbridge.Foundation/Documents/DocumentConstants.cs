namespace Celbridge.Documents;

/// <summary>
/// Shared constants for the document editor system.
/// </summary>
public static class DocumentConstants
{
    /// <summary>
    /// Built-in id of the code editor.
    /// </summary>
    public static readonly EditorId CodeEditorId = Packages.BuiltInEditors.CodeEditorId;

    /// <summary>
    /// Id of the last-resort text fallback view, used when no other editor claims the file.
    /// Has no registered factory.
    /// </summary>
    public static readonly EditorId TextBoxFallbackEditorId = new("celbridge.text-box-fallback");
}
