using Celbridge.Documents;

namespace Celbridge.HTML.Services;

public class HTMLPreviewProvider : IPreviewProvider
{
    private List<string> _supportedFileExtensions = new();
    public IReadOnlyList<string> SupportedFileExtensions => _supportedFileExtensions;

    public HTMLPreviewProvider()
    {
        _supportedFileExtensions.Add(".html");
    }

    public async Task<Result<string>> GeneratePreview(string text, IEditorPreview editorPreview)
    {
        await Task.CompletedTask;

        // Todo: Sanitize the HTML for security?

        return Result<string>.Ok(text);
    }
}
