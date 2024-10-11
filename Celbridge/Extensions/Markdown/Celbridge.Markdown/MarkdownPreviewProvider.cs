namespace Celbridge.Markdown;

public class MarkdownPreviewProvider : IPreviewProvider
{
    public async Task<Result<string>> GeneratePreviewHtml(string text)
    {
        await Task.CompletedTask;

        return Result<string>.Ok("This is a preview");
    }
}
