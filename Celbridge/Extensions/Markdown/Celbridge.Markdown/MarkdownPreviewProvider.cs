using Ganss.Xss;
using Markdig;

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

        var pipeline = new MarkdownPipelineBuilder()
            .DisableHtml() // Disable embedded HTML (to prevent embedding malicious scripts in Markdown)
            .UseAdvancedExtensions()
            .Build();

        var html = Markdig.Markdown.ToHtml(text, pipeline);

        // Sanitize the HTML to prevent XSS attacks
        var sanitizer = new HtmlSanitizer();
        var sanitizedHtml = sanitizer.Sanitize(html);

        return Result<string>.Ok(sanitizedHtml);
    }
}
