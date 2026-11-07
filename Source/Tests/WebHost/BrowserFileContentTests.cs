using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

[TestFixture]
public class BrowserFileContentTests
{
    [Test]
    public void TryParse_EmptyFile_YieldsNoBookmarks()
    {
        var result = BrowserFileContent.TryParse(string.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Bookmarks.Should().BeEmpty();
        result.Value.UnknownFields.Should().BeEmpty();
    }

    [Test]
    public void TryParse_AFileWithOnlyAComment_YieldsNoBookmarks()
    {
        var result = BrowserFileContent.TryParse("# A browser document.\n");

        result.IsSuccess.Should().BeTrue();
        result.Value.Bookmarks.Should().BeEmpty();
    }

    [Test]
    public void TryParse_ReadsEachBookmark()
    {
        const string Toml = """
            [[bookmarks]]
            url = "https://example.com/docs"
            name = "Docs"
            open_on_start = true

            [[bookmarks]]
            url = "http://localhost:5173"
            """;

        var result = BrowserFileContent.TryParse(Toml);

        result.IsSuccess.Should().BeTrue();
        result.Value.Bookmarks.Should().Equal(
            new BrowserBookmark("https://example.com/docs", "Docs", OpenOnStart: true),
            new BrowserBookmark("http://localhost:5173"));
    }

    [Test]
    public void TryParse_DropsABookmarkWithNoUrl()
    {
        const string Toml = """
            [[bookmarks]]
            name = "No address"

            [[bookmarks]]
            url = "https://example.com"
            """;

        var result = BrowserFileContent.TryParse(Toml);

        result.IsSuccess.Should().BeTrue();
        result.Value.Bookmarks.Should().ContainSingle()
            .Which.Url.Should().Be("https://example.com");
    }

    [Test]
    public void TryParse_ReportsKeysFromTheOldFormatAsUnknown()
    {
        const string Toml = """
            source_url = "https://example.com"
            show_url_bar = false

            [[bookmarks]]
            url = "https://example.com/docs"
            icon = "bs-book"
            """;

        var result = BrowserFileContent.TryParse(Toml);

        result.IsSuccess.Should().BeTrue();
        result.Value.UnknownFields.Should().BeEquivalentTo("source_url", "show_url_bar", "bookmarks.icon");
    }

    [TestCase("not valid = = toml")]
    [TestCase("bookmarks = \"nonsense\"")]
    public void TryParse_InvalidFile_Fails(string toml)
    {
        var result = BrowserFileContent.TryParse(toml);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void ToToml_RoundTripsEachBookmark()
    {
        var content = new BrowserFileContent
        {
            Bookmarks =
            [
                new BrowserBookmark("https://example.com/?q=\"a\\b\"", "Docs", OpenOnStart: true),
                new BrowserBookmark("https://example.org/")
            ]
        };

        var result = BrowserFileContent.TryParse(content.ToToml());

        result.IsSuccess.Should().BeTrue();
        result.Value.Bookmarks.Should().Equal(content.Bookmarks);
    }

    [Test]
    public void ToToml_LeavesOutAKeyAtItsDefaultValue()
    {
        var content = new BrowserFileContent
        {
            Bookmarks =
            [
                new BrowserBookmark("https://example.com/")
            ]
        };

        content.ToToml().Should().Be("[[bookmarks]]\nurl = \"https://example.com/\"\n");
    }

    [Test]
    public void ToToml_WithNoBookmarks_IsEmpty()
    {
        new BrowserFileContent().ToToml().Should().BeEmpty();
    }
}
