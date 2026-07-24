using Celbridge.Projects.Services;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Unit tests for PageManifestParser, which reads the [publish].path from a page's pages.toml manifest.
/// </summary>
[TestFixture]
public class PageManifestParserTests
{
    [Test]
    public void ParsePublishPath_ValidManifest_ReturnsPath()
    {
        var toml = """
            [publish]
            path = "my-site/home"
            """;

        var result = PageManifestParser.ParsePublishPath(toml);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("my-site/home");
    }

    [Test]
    public void ParsePublishPath_WhitespacePadding_IsTrimmed()
    {
        var toml = """
            [publish]
            path = "  my-site/home  "
            """;

        var result = PageManifestParser.ParsePublishPath(toml);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("my-site/home");
    }

    [Test]
    public void ParsePublishPath_MissingPublishSection_Fails()
    {
        var toml = """
            [other]
            path = "my-site/home"
            """;

        var result = PageManifestParser.ParsePublishPath(toml);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void ParsePublishPath_MissingPathKey_Fails()
    {
        var toml = """
            [publish]
            """;

        var result = PageManifestParser.ParsePublishPath(toml);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void ParsePublishPath_InvalidToml_Fails()
    {
        var toml = "[publish\npath = ";

        var result = PageManifestParser.ParsePublishPath(toml);

        result.IsFailure.Should().BeTrue();
    }
}
