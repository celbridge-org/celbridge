namespace Celbridge.Documents;

/// <summary>
/// Provides a HTML preview rendering of text data in a specific language (e.g. markdown, .md files)
/// </summary>
public interface IPreviewProvider
{
    /// <summary>
    /// Generates a HTML preview of the specified text.
    /// </summary>
    public abstract Task<Result<string>> GeneratePreview(string text, IEditorPreview editorPreview);
}
