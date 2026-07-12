namespace Celbridge.Packages;

/// <summary>
/// A custom webView-based document editor contribution.
/// The extension provides the entire UI via an HTML entry point.
/// Communicates with the host via the IHostDocument JSON-RPC protocol.
/// </summary>
public partial record CustomDocumentEditorContribution : DocumentEditorContribution
{
    /// <summary>
    /// Entry point for the custom editor (e.g., "index.html").
    /// </summary>
    public string EntryPoint { get; init; } = "index.html";

    /// <summary>
    /// Whether this editor handles binary file content.
    /// When true, content is transferred as base64 and saved/loaded as raw bytes.
    /// </summary>
    public bool Binary { get; init; } = false;

    /// <summary>
    /// Whether this editor sources its content from outside the file bytes
    /// (e.g., the JS fetches the file directly from the project virtual host,
    /// or a host-side IDocumentContentProvider supplies generated content).
    /// When true, the host returns an empty content string from InitializeAsync / LoadAsync
    /// unless a registered IDocumentContentProvider matches the resource.
    /// </summary>
    public bool ExternalContent { get; init; } = false;

    /// <summary>
    /// Package-defined options parsed from the [options] table of the document manifest.
    /// Keys and values are opaque to the host, the editor interprets them.
    /// </summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } = EmptyOptions;

    /// <summary>
    /// Utility metadata parsed from the [utility] section, or null for an ordinary file-type editor.
    /// When present this contribution is a utility document rather than an extension-claiming editor.
    /// </summary>
    public UtilityDescriptor? UtilityDescriptor { get; init; }

    /// <summary>
    /// Whether this contribution is a utility document, derived from the presence of a UtilityDescriptor.
    /// </summary>
    public bool IsUtility => UtilityDescriptor is not null;

    private static readonly IReadOnlyDictionary<string, string> EmptyOptions =
        new Dictionary<string, string>();
}
