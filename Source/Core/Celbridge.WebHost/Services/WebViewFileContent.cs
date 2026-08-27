using System.Text;
using Celbridge.Utilities;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

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

    /// <summary>
    /// The bookmarked pages, in the order their buttons appear in the bookmarks bar.
    /// </summary>
    public IReadOnlyList<WebViewBookmark> Bookmarks { get; init; } = Array.Empty<WebViewBookmark>();

    /// <summary>
    /// Parses the TOML body of a .webview file. An empty file or a missing key
    /// yields the default value rather than a failure, so a brand-new file (or a
    /// hand-edited blank file) still loads. Unrecognised keys are ignored, and a
    /// malformed bookmark is dropped rather than failing the parse.
    /// </summary>
    public static Result<WebViewFileContent> TryParse(string toml)
    {
        if (string.IsNullOrWhiteSpace(toml))
        {
            return new WebViewFileContent(string.Empty);
        }

        // Tomlyn rejects bare-\r line terminators, so normalize before parsing.
        var text = LineEndingHelper.ConvertLineEndings(toml, "\n");

        var parse = SyntaxParser.Parse(text);
        if (parse.HasErrors)
        {
            var errors = string.Join("; ", parse.Diagnostics.Select(d => d.ToString()));
            return Result.Fail($"Invalid TOML: {errors}");
        }

        var root = TomlSerializer.Deserialize<TomlTable>(text);
        if (root is null)
        {
            return Result.Fail("Invalid TOML: failed to deserialize.");
        }

        var sourceUrl = string.Empty;
        if (root.TryGetValue(SourceUrlKey, out var sourceUrlValue))
        {
            if (sourceUrlValue is not string sourceUrlText)
            {
                return Result.Fail($"Key '{SourceUrlKey}' must be a string.");
            }
            sourceUrl = sourceUrlText;
        }

        var showUrlBarResult = TryReadFlag(root, ShowUrlBarKey);
        if (showUrlBarResult.IsFailure)
        {
            return Result.Fail($"Failed to read key '{ShowUrlBarKey}'")
                .WithErrors(showUrlBarResult);
        }
        var showUrlBar = showUrlBarResult.Value;

        var showBookmarksBarResult = TryReadFlag(root, ShowBookmarksBarKey);
        if (showBookmarksBarResult.IsFailure)
        {
            return Result.Fail($"Failed to read key '{ShowBookmarksBarKey}'")
                .WithErrors(showBookmarksBarResult);
        }
        var showBookmarksBar = showBookmarksBarResult.Value;

        var bookmarks = ReadBookmarks(root);

        return new WebViewFileContent(sourceUrl, showUrlBar, showBookmarksBar)
        {
            Bookmarks = bookmarks
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

    // Reads a boolean key, defaulting to true when it is absent.
    private static Result<bool> TryReadFlag(TomlTable root, string key)
    {
        if (!root.TryGetValue(key, out var value))
        {
            return true;
        }

        if (value is not bool flag)
        {
            return Result.Fail($"Key '{key}' must be a boolean.");
        }

        return flag;
    }

    private static List<WebViewBookmark> ReadBookmarks(TomlTable root)
    {
        var bookmarks = new List<WebViewBookmark>();

        if (!root.TryGetValue(BookmarksKey, out var bookmarksValue))
        {
            return bookmarks;
        }

        // Anything other than the array of tables this writes is passed over, for the same reason a single
        // malformed entry is: the bookmarks are chrome, and none of them are worth failing the page over.
        if (bookmarksValue is not TomlTableArray bookmarkTables)
        {
            return bookmarks;
        }

        foreach (var bookmarkTable in bookmarkTables)
        {
            var bookmark = TryReadBookmark(bookmarkTable);
            if (bookmark is null)
            {
                continue;
            }

            bookmarks.Add(bookmark);
        }

        return bookmarks;
    }

    // A bookmark with no usable URL has nothing to navigate to, so it is dropped. The name and icon are
    // both optional, and a value of the wrong type is read as unset.
    private static WebViewBookmark? TryReadBookmark(TomlTable table)
    {
        var url = ReadOptionalString(table, BookmarkUrlKey);
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var name = ReadOptionalString(table, BookmarkNameKey);
        var icon = ReadOptionalString(table, BookmarkIconKey);

        return new WebViewBookmark(url.Trim(), name.Trim(), icon.Trim());
    }

    private static string ReadOptionalString(TomlTable table, string key)
    {
        if (table.TryGetValue(key, out var value)
            && value is string text)
        {
            return text;
        }

        return string.Empty;
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
