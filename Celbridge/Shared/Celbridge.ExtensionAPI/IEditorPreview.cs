namespace Celbridge.ExtensionAPI;

public interface IEditorPreview
{
    /// <summary>
    /// Convert AsciiDoc content to HTML.
    /// </summary>
    Task<Result<string>> ConvertAsciiDocToHTML(string asciiDoc);
}
