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
    public void ComputeBasePath_ReturnsRelativePath_WhenDocumentIsUnderProjectFolder()
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "myproject");
        var documentPath = Path.Combine(projectFolder, "docs", "notes.md");

        var basePath = _renderer.ComputeBasePath(documentPath, projectFolder);

        basePath.Should().Be("docs");
    }

    [Test]
    public void ComputeBasePath_ReturnsRelativePath_ForNestedSubfolder()
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "myproject");
        var documentPath = Path.Combine(projectFolder, "docs", "guides", "notes.md");

        var basePath = _renderer.ComputeBasePath(documentPath, projectFolder);

        basePath.Should().Be("docs/guides");
    }

    [Test]
    public void ComputeBasePath_ReturnsEmpty_WhenDocumentIsOutsideProjectFolder()
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "myproject");
        var documentPath = Path.Combine(Path.GetTempPath(), "other", "notes.md");

        var basePath = _renderer.ComputeBasePath(documentPath, projectFolder);

        basePath.Should().BeEmpty();
    }

    [Test]
    public void ComputeBasePath_ReturnsEmpty_WhenPathsAreEmpty()
    {
        var basePath = _renderer.ComputeBasePath(string.Empty, string.Empty);

        basePath.Should().BeEmpty();
    }
}
