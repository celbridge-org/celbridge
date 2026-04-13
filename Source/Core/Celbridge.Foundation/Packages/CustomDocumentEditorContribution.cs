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
    /// Whether browser developer tools are allowed for this editor.
    /// Defaults to true so extension authors can debug their editors.
    /// The global webview-dev-tools feature flag must also be enabled for DevTools to be accessible.
    /// </summary>
    public bool DevToolsEnabled { get; init; } = true;

    /// <summary>
    /// Whether this editor handles binary file content.
    /// When true, content is transferred as base64 and saved/loaded as raw bytes.
    /// </summary>
    public bool Binary { get; init; } = false;
}
