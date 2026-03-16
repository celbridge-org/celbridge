using Celbridge.Markdown.Services;

namespace Celbridge.Tests.Markdown;

[TestFixture]
public class MarkdownPreviewRendererTests
{
    private MarkdownPreviewRenderer _renderer = null!;

    [SetUp]
    public void Setup()
    {
        _renderer = new MarkdownPreviewRenderer();
    }

    [Test]
    public void PreviewPageUrl_ReturnsExpectedUrl()
    {
        _renderer.PreviewPageUrl.Should().Be("https://markdown-preview.celbridge/index.html");
    }
}
