using Tomlyn.Serialization;

namespace Celbridge.WebHost;

/// <summary>
/// One [[bookmarks]] entry, naming a page the document's bookmark list offers.
/// </summary>
internal sealed record BrowserBookmarkEntry
{
    public string? Url { get; init; }

    // Label the bookmark list shows. Left out of the file when the user set none.
    public string? Name { get; init; }

    // Opens the bookmark in a tab when a document with no saved tabs opens.
    public bool? OpenOnStart { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// The shape of a .browser file, deserialized by Tomlyn. The property names are the file's known keys under their
/// snake_case spelling. Every other key lands in an UnknownKeys bag. BrowserFileContent is the type the rest of the
/// application reads.
/// </summary>
internal sealed record BrowserDocumentFile
{
    public List<BrowserBookmarkEntry> Bookmarks { get; init; } = new();

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}
