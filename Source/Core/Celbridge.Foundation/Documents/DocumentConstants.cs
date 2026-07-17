namespace Celbridge.Documents;

/// <summary>
/// Shared constants for the document editor system.
/// </summary>
public static class DocumentConstants
{
    /// <summary>
    /// Built-in id of the code editor.
    /// </summary>
    public static readonly EditorInstanceId CodeEditorId = Packages.BuiltInEditors.CodeEditorId;

    /// <summary>
    /// Id of the last-resort text fallback view, used when no other editor claims the file.
    /// Has no registered factory.
    /// </summary>
    public static readonly EditorInstanceId TextBoxFallbackEditorId = new("celbridge.text-box-fallback");

    /// <summary>
    /// Returns the workspace settings key for the user's preferred document editor for a file extension.
    /// </summary>
    public static string GetEditorPreferenceKey(string fileExtension) =>
        $"DocumentEditorPreference:{fileExtension}";
}
