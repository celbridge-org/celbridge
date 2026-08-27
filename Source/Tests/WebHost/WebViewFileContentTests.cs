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

        // Compared field by field: the record carries a bookmark list, which its synthesized equality
        // compares by reference.
        result.Value.Should().BeEquivalentTo(content);
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
    public void TryParse_ReadsBookmarks()
    {
        var toml =
            """
            source_url = "https://example.com"

            [[bookmarks]]
            url = "https://example.com/docs"
            name = "Docs"
            icon = "bs-book"

            [[bookmarks]]
            url = "http://localhost:5173"
            """;

        var result = WebViewFileContent.TryParse(toml);

        result.IsSuccess.Should().BeTrue();
        result.Value.Bookmarks.Should().HaveCount(2);
        result.Value.Bookmarks[0].Should().Be(new WebViewBookmark("https://example.com/docs", "Docs", "bs-book"));
        result.Value.Bookmarks[1].Should().Be(new WebViewBookmark("http://localhost:5173"));
    }

    [Test]
    public void TryParse_MissingBookmarks_ReturnsEmpty()
    {
        var result = WebViewFileContent.TryParse("source_url = \"https://example.com\"");

        result.IsSuccess.Should().BeTrue();
        result.Value.Bookmarks.Should().BeEmpty();
        result.Value.ShowBookmarksBar.Should().BeTrue();
    }

    [Test]
    public void TryParse_DropsBookmarkWithNoUrl()
    {
        // One malformed entry must not stop the document from opening, so it is dropped and the rest of
        // the bookmarks load.
        var toml =
            """
            [[bookmarks]]
            name = "No URL"

            [[bookmarks]]
            url = "https://example.com"
            """;

        var result = WebViewFileContent.TryParse(toml);

        result.IsSuccess.Should().BeTrue();
        result.Value.Bookmarks.Should().ContainSingle();
        result.Value.Bookmarks[0].Url.Should().Be("https://example.com");
    }

    [Test]
    public void TryParse_IgnoresBookmarksOfWrongType()
    {
        var result = WebViewFileContent.TryParse("bookmarks = \"nonsense\"");

        result.IsSuccess.Should().BeTrue();
        result.Value.Bookmarks.Should().BeEmpty();
    }

    [Test]
    public void TryParse_FailsOnWrongShowBookmarksBarType()
    {
        var result = WebViewFileContent.TryParse("show_bookmarks_bar = \"yes\"");

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void ToToml_RoundTripsBookmarks()
    {
        var bookmarks = new List<WebViewBookmark>
        {
            new("https://example.com/docs", "Docs", "bs-book"),
            new("http://127.0.0.1:8080")
        };

        var content = new WebViewFileContent("https://example.com", ShowBookmarksBar: false)
        {
            Bookmarks = bookmarks
        };

        var result = WebViewFileContent.TryParse(content.ToToml());

        result.IsSuccess.Should().BeTrue();
        result.Value.ShowBookmarksBar.Should().BeFalse();
        result.Value.Bookmarks.Should().BeEquivalentTo(bookmarks, options => options.WithStrictOrdering());
    }

    [Test]
    public void ToToml_EndsWithSingleTrailingNewline()
    {
        var toml = new WebViewFileContent("https://example.com").ToToml();

        toml.Should().EndWith("\n");
        toml.Should().NotEndWith("\n\n");
    }
}
