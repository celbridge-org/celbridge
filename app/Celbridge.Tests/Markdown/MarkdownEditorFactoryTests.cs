using Celbridge.Markdown.Services;

namespace Celbridge.Tests.Markdown;

[TestFixture]
public class MarkdownEditorFactoryTests
{
    private MarkdownEditorFactory _factory = null!;

    [SetUp]
    public void Setup()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        _factory = new MarkdownEditorFactory(serviceProvider);
    }

    [Test]
    public void SupportedExtensions_ContainsMdAndMarkdown()
    {
        _factory.SupportedExtensions.Should().Contain(".md");
        _factory.SupportedExtensions.Should().Contain(".markdown");
    }

    [Test]
    public void CanHandle_ReturnsTrue_ForMdExtension()
    {
        var fileResource = new ResourceKey("document.md");

        var result = _factory.CanHandle(fileResource, string.Empty);

        result.Should().BeTrue();
    }

    [Test]
    public void CanHandle_ReturnsTrue_ForMarkdownExtension()
    {
        var fileResource = new ResourceKey("document.markdown");

        var result = _factory.CanHandle(fileResource, string.Empty);

        result.Should().BeTrue();
    }

    [Test]
    public void CanHandle_ReturnsFalse_ForUnsupportedExtension()
    {
        var fileResource = new ResourceKey("document.txt");

        var result = _factory.CanHandle(fileResource, string.Empty);

        result.Should().BeFalse();
    }
}
