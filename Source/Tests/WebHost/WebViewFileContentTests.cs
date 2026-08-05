using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

[TestFixture]
public class WebViewFileContentTests
{
    [Test]
    public void TryParse_EmptyText_ReturnsDefaults()
    {
        var result = WebViewFileContent.TryParse(string.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.SourceUrl.Should().BeEmpty();
        result.Value.ShowUrlBar.Should().BeTrue();
    }

    [Test]
    public void TryParse_SourceUrlOnly_DefaultsShowUrlBar()
    {
        var result = WebViewFileContent.TryParse("source_url = \"https://example.com\"");

        result.IsSuccess.Should().BeTrue();
        result.Value.SourceUrl.Should().Be("https://example.com");
        result.Value.ShowUrlBar.Should().BeTrue();
    }

    [Test]
    public void TryParse_ReadsBothKeys()
    {
        var toml =
            """
            source_url = "https://example.com"
            show_url_bar = false
            """;

        var result = WebViewFileContent.TryParse(toml);

        result.IsSuccess.Should().BeTrue();
        result.Value.SourceUrl.Should().Be("https://example.com");
        result.Value.ShowUrlBar.Should().BeFalse();
    }

    [Test]
    public void TryParse_IgnoresComments()
    {
        var toml =
            """
            # Opens a web page in an embedded browser.
            source_url = "https://example.com"
            """;

        var result = WebViewFileContent.TryParse(toml);

        result.IsSuccess.Should().BeTrue();
        result.Value.SourceUrl.Should().Be("https://example.com");
    }

    [Test]
    public void TryParse_IgnoresUnrecognizedKeys()
    {
        var toml =
            """
            source_url = "https://example.com"
            future_key = "ignored"
            """;

        var result = WebViewFileContent.TryParse(toml);

        result.IsSuccess.Should().BeTrue();
        result.Value.SourceUrl.Should().Be("https://example.com");
    }

    [Test]
    public void TryParse_FailsOnInvalidToml()
    {
        var result = WebViewFileContent.TryParse("source_url = not quoted");

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void TryParse_FailsOnLegacyJson()
    {
        // The JSON format was retired in a clean cut; a legacy file surfaces a
        // parse failure rather than silently loading blank.
        var result = WebViewFileContent.TryParse("{ \"sourceUrl\": \"https://example.com\" }");

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void TryParse_FailsOnWrongSourceUrlType()
    {
        var result = WebViewFileContent.TryParse("source_url = true");

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void TryParse_FailsOnWrongShowUrlBarType()
    {
        var result = WebViewFileContent.TryParse("show_url_bar = \"yes\"");

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void ToToml_RoundTrips()
    {
        var content = new WebViewFileContent("https://example.com/path?q=1", ShowUrlBar: false);

        var result = WebViewFileContent.TryParse(content.ToToml());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(content);
    }

    [Test]
    public void ToToml_EscapesQuotesAndBackslashes()
    {
        var content = new WebViewFileContent("https://example.com/?q=\"a\\b\"");

        var result = WebViewFileContent.TryParse(content.ToToml());

        result.IsSuccess.Should().BeTrue();
        result.Value.SourceUrl.Should().Be("https://example.com/?q=\"a\\b\"");
    }

    [Test]
    public void ToToml_EndsWithSingleTrailingNewline()
    {
        var toml = new WebViewFileContent("https://example.com").ToToml();

        toml.Should().EndWith("\n");
        toml.Should().NotEndWith("\n\n");
    }
}
