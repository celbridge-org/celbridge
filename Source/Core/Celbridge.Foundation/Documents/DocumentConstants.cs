namespace Celbridge.Documents;

/// <summary>
/// Shared constants for the document editor system.
/// </summary>
public static class DocumentConstants
{
    /// <summary>
    /// Id of the bundled code editor.
    /// </summary>
    public static readonly DocumentEditorId CodeEditorId = new("celbridge.code-editor.code-document");

    /// <summary>
    /// Sidecar frontmatter field that records the user's per-file editor choice
    /// (last "Open with..." selection). Shared so the read path (preference
    /// store) and write path (OpenWith menu) stay in lockstep.
    /// </summary>
    public const string SidecarEditorFieldName = "editor";

    /// <summary>
    /// Returns the workspace settings key for the user's preferred document editor for a file extension.
    /// </summary>
    public static string GetEditorPreferenceKey(string fileExtension) =>
        $"DocumentEditorPreference:{fileExtension}";
}
