namespace Celbridge.Markdown;

public class MarkdownPreviewProvider : PreviewProvider
{
    public MarkdownPreviewProvider()
    {
        SupportedFileExtensions.Add(".md");
    }

    public override async Task<Result<string>> GeneratePreview(string text)
    {
        await Task.CompletedTask;

        return Result<string>.Ok(text);
    }
}
