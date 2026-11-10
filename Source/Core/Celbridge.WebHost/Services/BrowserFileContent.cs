using System.Text;
using System.Text.Json;
using Celbridge.Utilities;
using Tomlyn;

namespace Celbridge.WebHost;

/// <summary>
/// A bookmarked page in a .browser document. Url is the address to open, and Name is the label in the bookmark list.
/// OpenOnStart opens the page in a tab when a document with no saved tabs opens. Favicon is the site's icon as a PNG
/// file, or null for a bookmark that shows the default icon.
/// </summary>
public sealed record BrowserBookmark(string Url, string Name = "", bool OpenOnStart = false, byte[]? Favicon = null)
{
    /// <summary>
    /// The size limit, in bytes, for an icon in the file. A scaled bookmark icon is far smaller.
    /// </summary>
    public const int MaximumFaviconBytes = 16 * 1024;

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// Returns true for an icon the file can store: a PNG file within MaximumFaviconBytes.
    /// </summary>
    public static bool IsStorableFavicon(ReadOnlySpan<byte> favicon)
    {
        return favicon.Length <= MaximumFaviconBytes
            && favicon.StartsWith(PngSignature);
    }
}

/// <summary>
/// Storage shape for a .browser file: a TOML document listing the document's bookmarks.
/// </summary>
public sealed record BrowserFileContent
{
    private const string BookmarksKey = "bookmarks";
    private const string BookmarkUrlKey = "url";
    private const string BookmarkNameKey = "name";
    private const string BookmarkOpenOnStartKey = "open_on_start";
    private const string BookmarkFaviconKey = "favicon";

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
    /// usable URL is dropped. The parse also drops a favicon unless the value is a base64 PNG within
    /// MaximumFaviconBytes. That bookmark then shows the default icon.
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

            // The long value goes last, below the keys someone edits by hand.
            if (bookmark.Favicon is { Length: > 0 } favicon)
            {
                AppendStringKey(builder, BookmarkFaviconKey, Convert.ToBase64String(favicon));
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
            var favicon = ReadFavicon(entry.Favicon);

            bookmarks.Add(new BrowserBookmark(url.Trim(), name.Trim(), entry.OpenOnStart ?? false, favicon));
        }

        return bookmarks;
    }

    // Decodes a favicon value. Returns null unless the value is a base64 PNG within the size limit.
    private static byte[]? ReadFavicon(string? value)
    {
        // Base64 takes four characters for every three bytes. A value longer than this decodes to more than the limit.
        var maximumLength = (BrowserBookmark.MaximumFaviconBytes + 2) / 3 * 4;
        if (string.IsNullOrEmpty(value)
            || value.Length > maximumLength)
        {
            return null;
        }

        var buffer = new byte[value.Length / 4 * 3 + 3];
        if (!Convert.TryFromBase64String(value, buffer, out var length))
        {
            return null;
        }

        var favicon = buffer.AsSpan(0, length);
        if (!BrowserBookmark.IsStorableFavicon(favicon))
        {
            return null;
        }

        return favicon.ToArray();
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
