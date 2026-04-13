namespace Celbridge.Documents;

/// <summary>
/// Shared constants for the document editor system.
/// </summary>
public static class DocumentConstants
{
    /// <summary>
    /// Returns the workspace settings key for the user's preferred document editor for a file extension.
    /// </summary>
    public static string GetEditorPreferenceKey(string fileExtension) =>
        $"DocumentEditorPreference:{fileExtension}";
}
