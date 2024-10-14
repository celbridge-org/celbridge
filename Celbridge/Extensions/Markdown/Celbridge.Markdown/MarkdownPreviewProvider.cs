using Markdig;
using Windows.Web.UI;

namespace Celbridge.Markdown;

public class MarkdownPreviewProvider : PreviewProvider
{
    public MarkdownPreviewProvider()
    {
        SupportedFileExtensions.Add(".md");
    }

    public override async Task<Result<string>> GeneratePreview(string text, IEditorPreview editorPreview)
    {
        await Task.CompletedTask;

        var pipeline = new MarkdownPipelineBuilder()
            .DisableHtml() // Disable embedded HTML (to prevent embedding malicious scripts in Markdown)
            .UseAdvancedExtensions()
            .UseMediaLinks()
            .Build();

        var html = Markdig.Markdown.ToHtml(text, pipeline);

        return Result<string>.Ok(html);
    }
}
