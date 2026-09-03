using System.Text;
using System.Text.Json;
using Celbridge.Utilities;
using Tomlyn;

namespace Celbridge.WebHost;

/// <summary>
/// A bookmarked page in a .webview document: the URL to navigate to, the display name its toolbar button
/// carries, and an optional prefixed icon name for the glyph shown beside that name.
/// </summary>
public sealed record WebViewBookmark(string Url, string Name = "", string Icon = "");

/// <summary>
/// Storage shape for a .webview file: a TOML document carrying the Home URL the
/// embedded WebView opens, the bookmarks it offers, and how the document presents
/// its browser chrome.
/// </summary>
public sealed record WebViewFileContent(
    string SourceUrl,
    bool ShowUrlBar = true,
    bool ShowBookmarksBar = true)
{
    private const string SourceUrlKey = "source_url";
    private const string ShowUrlBarKey = "show_url_bar";
    private const string ShowBookmarksBarKey = "show_bookmarks_bar";
    private const string BookmarksKey = "bookmarks";
    private const string BookmarkUrlKey = "url";
    private const string BookmarkNameKey = "name";
    private const string BookmarkIconKey = "icon";

    // File keys are the snake_case spelling of the WebViewDocumentFile property names.
    private static readonly TomlSerializerOptions DocumentOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// The bookmarked pages, in the order their buttons appear in the bookmarks bar.
    /// </summary>
    public IReadOnlyList<WebViewBookmark> Bookmarks { get; init; } = Array.Empty<WebViewBookmark>();

    /// <summary>
    /// Keys the file declared that the host does not define. The document still opens, so this is
    /// advisory. Note that ToToml writes only the known keys, so an unknown key is lost on save.
    /// </summary>
    public IReadOnlyList<string> UnknownFields { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Parses the TOML body of a .webview file. An empty file or a missing key
    /// yields the default value rather than a failure, so a brand-new file (or a
    /// hand-edited blank file) still loads. A bookmark with no usable URL is dropped rather than
    /// failing the parse.
    /// </summary>
    public static Result<WebViewFileContent> TryParse(string toml)
    {
        if (string.IsNullOrWhiteSpace(toml))
        {
            return new WebViewFileContent(string.Empty);
        }

        // Tomlyn rejects bare-\r line terminators, so normalize before parsing.
        var text = LineEndingHelper.ConvertLineEndings(toml, "\n");

        WebViewDocumentFile? document;
        try
        {
            document = TomlSerializer.Deserialize<WebViewDocumentFile>(text, DocumentOptions);
        }
        catch (TomlException exception)
        {
            // A shape error carries no diagnostic, only a message, so fall back to it.
            var detail = exception.Message;
            if (exception.Diagnostics.Count > 0)
            {
                detail = string.Join("; ", exception.Diagnostics.Select(diagnostic => diagnostic.ToString()));
            }

            return Result.Fail($"Invalid TOML: {detail}");
        }

        if (document is null)
        {
            return Result.Fail("Invalid TOML: failed to deserialize.");
        }

        return new WebViewFileContent(
            document.SourceUrl ?? string.Empty,
            document.ShowUrlBar ?? true,
            document.ShowBookmarksBar ?? true)
        {
            Bookmarks = ReadBookmarks(document),
            UnknownFields = CollectUnknownFields(document)
        };
    }

    /// <summary>
    /// Serialises this content as the canonical .webview TOML document.
    /// Trailing newline matches the convention used by the other text-storage
    /// roundtrips.
    /// </summary>
    public string ToToml()
    {
        var builder = new StringBuilder();

        AppendStringKey(builder, SourceUrlKey, SourceUrl);
        AppendFlagKey(builder, ShowUrlBarKey, ShowUrlBar);
        AppendFlagKey(builder, ShowBookmarksBarKey, ShowBookmarksBar);

        foreach (var bookmark in Bookmarks)
        {
            // Blank line ahead of each table so the keys above stay legible in a hand-edited file.
            builder.Append('\n');
            builder.Append("[[");
            builder.Append(BookmarksKey);
            builder.Append("]]\n");

            AppendStringKey(builder, BookmarkUrlKey, bookmark.Url);

            // An unset name or icon is left out rather than written as a blank string, so the file records
            // only what the user actually chose.
            if (!string.IsNullOrEmpty(bookmark.Name))
            {
                AppendStringKey(builder, BookmarkNameKey, bookmark.Name);
            }

            if (!string.IsNullOrEmpty(bookmark.Icon))
            {
                AppendStringKey(builder, BookmarkIconKey, bookmark.Icon);
            }
        }

        return builder.ToString();
    }

    // A bookmark with no usable URL has nothing to navigate to, so it is dropped. The name and icon are
    // both optional.
    private static IReadOnlyList<WebViewBookmark> ReadBookmarks(WebViewDocumentFile document)
    {
        var bookmarks = new List<WebViewBookmark>();

        foreach (var entry in document.Bookmarks)
        {
            var url = entry.Url ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var name = entry.Name ?? string.Empty;
            var icon = entry.Icon ?? string.Empty;

            bookmarks.Add(new WebViewBookmark(url.Trim(), name.Trim(), icon.Trim()));
        }

        return bookmarks;
    }

    private static IReadOnlyList<string> CollectUnknownFields(WebViewDocumentFile document)
    {
        var unknownFields = new List<string>();

        unknownFields.AddRange(document.UnknownKeys.Keys);

        foreach (var entry in document.Bookmarks)
        {
            foreach (var key in entry.UnknownKeys.Keys)
            {
                unknownFields.Add($"{BookmarksKey}.{key}");
            }
        }

        return unknownFields.AsReadOnly();
    }

    private static void AppendStringKey(StringBuilder builder, string key, string value)
    {
        builder.Append(key);
        builder.Append(" = ");
        builder.Append(TomlStringEncoder.EncodeBasicString(value));
        builder.Append('\n');
    }

    private static void AppendFlagKey(StringBuilder builder, string key, bool value)
    {
        builder.Append(key);
        builder.Append(" = ");
        builder.Append(value ? "true" : "false");
        builder.Append('\n');
    }
}
