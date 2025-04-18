using System.Text.RegularExpressions;
using Celbridge.Documents;
using Markdig;

namespace Celbridge.Markdown.Services;

public class MarkdownPreviewProvider : IPreviewProvider
{
    private List<string> _supportedFileExtensions = new();
    public IReadOnlyList<string> SupportedFileExtensions => _supportedFileExtensions;

    public MarkdownPreviewProvider()
    {
        _supportedFileExtensions.Add(".md");
    }

    public async Task<Result<string>> GeneratePreview(string text, IEditorPreview editorPreview)
    {
        await Task.CompletedTask;

        // Remove any HTML comments from the text before processing.
        // This is required because the DisableHtml() option causes HTML comments to be output as raw text.
        if (text.Contains("<!--"))
        {
            text = Regex.Replace(text, @"<!--.*?-->", "", RegexOptions.Singleline);
        }

        var pipeline = new MarkdownPipelineBuilder()
            .DisableHtml() // Disable embedded HTML (to prevent embedding malicious scripts in Markdown)
            .UseAdvancedExtensions()
            .UseMediaLinks()
            .UseTableOfContent()
            .Build();

        var html = Markdig.Markdown.ToHtml(text, pipeline);

        return Result<string>.Ok(html);
    }
}
