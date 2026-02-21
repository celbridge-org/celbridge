using Celbridge.Documents;

namespace Celbridge.HTML.Services;

public class HTMLPreviewProvider : IHTMLPreviewProvider
{
    public HTMLPreviewProvider()
    {}

    public async Task<Result<string>> GeneratePreview(string text)
    {
        await Task.CompletedTask;

        // Todo: Sanitize the HTML for security?

        return Result<string>.Ok(text);
    }
}
