using Tomlyn.Serialization;

namespace Celbridge.WebHost;

/// <summary>
/// One [[bookmarks]] entry, naming a page in the document's bookmark list.
/// </summary>
internal sealed record BrowserBookmarkEntry
{
    public string? Url { get; init; }

    // The label in the bookmark list. Left out of the file when the user sets none.
    public string? Name { get; init; }

    // Opens the bookmark in a tab when a document with no saved tabs opens.
    public bool? OpenOnStart { get; init; }

    // The site's icon as a PNG file in base64. Left out of the file until a tab shows the bookmark's page.
    public string? Favicon { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// The shape of a .browser file, deserialized by Tomlyn. The property names are the file's known keys under their
/// snake_case spelling. Every other key lands in an UnknownKeys bag. The rest of the application reads
/// BrowserFileContent.
/// </summary>
internal sealed record BrowserDocumentFile
{
    public List<BrowserBookmarkEntry> Bookmarks { get; init; } = new();

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}
