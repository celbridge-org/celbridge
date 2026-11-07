using System.Text;
using System.Text.Json;
using Celbridge.Utilities;
using Tomlyn;

namespace Celbridge.WebHost;

/// <summary>
/// A bookmarked page in a .browser document. Url is the address to open, and Name is the label in the bookmark list.
/// OpenOnStart opens the page in a tab when a document with no saved tabs opens.
/// </summary>
public sealed record BrowserBookmark(string Url, string Name = "", bool OpenOnStart = false);

/// <summary>
/// Storage shape for a .browser file: a TOML document listing the document's bookmarks.
/// </summary>
public sealed record BrowserFileContent
{
    private const string BookmarksKey = "bookmarks";
    private const string BookmarkUrlKey = "url";
    private const string BookmarkNameKey = "name";
    private const string BookmarkOpenOnStartKey = "open_on_start";

    // File keys are the snake_case spelling of the BrowserDocumentFile property names.
    private static readonly TomlSerializerOptions DocumentOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// The bookmarked pages, in the order the bookmark list shows them.
    /// </summary>
    public IReadOnlyList<BrowserBookmark> Bookmarks { get; init; } = Array.Empty<BrowserBookmark>();

    /// <summary>
    /// The file's unknown keys. The document still opens, and ToToml drops these keys on the next save.
    /// </summary>
    public IReadOnlyList<string> UnknownFields { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Parses the TOML body of a .browser file. An empty file yields a document with no bookmarks. A bookmark with no
    /// usable URL is dropped.
    /// </summary>
    public static Result<BrowserFileContent> TryParse(string toml)
    {
        if (string.IsNullOrWhiteSpace(toml))
        {
            return new BrowserFileContent();
        }

        // Tomlyn rejects bare-\r line terminators, so normalize before parsing.
        var text = LineEndingHelper.ConvertLineEndings(toml, "\n");

        BrowserDocumentFile? document;
        try
        {
            document = TomlSerializer.Deserialize<BrowserDocumentFile>(text, DocumentOptions);
        }
        catch (TomlException exception)
        {
            // A shape error has only a message, so the detail falls back to the message.
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

        return new BrowserFileContent
        {
            Bookmarks = ReadBookmarks(document),
            UnknownFields = CollectUnknownFields(document)
        };
    }

    /// <summary>
    /// Serializes this content as the canonical .browser TOML document. A key at its default value is left out, so a
    /// shared file records only what someone chose.
    /// </summary>
    public string ToToml()
    {
        var builder = new StringBuilder();

        foreach (var bookmark in Bookmarks)
        {
            // A blank line between tables keeps a hand-edited file legible.
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append("[[");
            builder.Append(BookmarksKey);
            builder.Append("]]\n");

            AppendStringKey(builder, BookmarkUrlKey, bookmark.Url);

            if (!string.IsNullOrEmpty(bookmark.Name))
            {
                AppendStringKey(builder, BookmarkNameKey, bookmark.Name);
            }

            if (bookmark.OpenOnStart)
            {
                builder.Append(BookmarkOpenOnStartKey);
                builder.Append(" = true\n");
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<BrowserBookmark> ReadBookmarks(BrowserDocumentFile document)
    {
        var bookmarks = new List<BrowserBookmark>();

        foreach (var entry in document.Bookmarks)
        {
            var url = entry.Url ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var name = entry.Name ?? string.Empty;

            bookmarks.Add(new BrowserBookmark(url.Trim(), name.Trim(), entry.OpenOnStart ?? false));
        }

        return bookmarks;
    }

    private static IReadOnlyList<string> CollectUnknownFields(BrowserDocumentFile document)
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
}
