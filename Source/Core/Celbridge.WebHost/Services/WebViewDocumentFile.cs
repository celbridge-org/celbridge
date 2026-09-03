using Tomlyn.Serialization;

namespace Celbridge.WebHost;

/// <summary>
/// One [[bookmarks]] entry, naming a page the bookmarks bar offers.
/// </summary>
internal sealed record WebViewBookmarkEntry
{
    public string? Url { get; init; }

    // Label the toolbar button carries. Left out of the file when the user set none.
    public string? Name { get; init; }

    // Prefixed icon name, "<font>-<name>", for the glyph beside the label.
    public string? Icon { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// The shape of a .webview file, deserialized by Tomlyn. The property names are the file's known keys
/// under their snake_case spelling, and every other key lands in an UnknownKeys bag rather than being
/// dropped. WebViewFileContent is what the rest of the host reads; this is only the storage shape.
/// </summary>
internal sealed record WebViewDocumentFile
{
    // Home URL the embedded WebView opens.
    public string? SourceUrl { get; init; }

    public bool? ShowUrlBar { get; init; }
    public bool? ShowBookmarksBar { get; init; }

    public List<WebViewBookmarkEntry> Bookmarks { get; init; } = new();

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}
