namespace Celbridge.Extensions;

/// <summary>
/// A custom webView-based document editor contribution.
/// The extension provides the entire UI via an HTML entry point.
/// Communicates with the host via the IHostDocument JSON-RPC protocol.
/// </summary>
public partial record CustomDocumentContribution : DocumentContribution
{
    /// <summary>
    /// Entry point for the custom editor (e.g., "index.html").
    /// </summary>
    public string EntryPoint { get; init; } = "index.html";
}
