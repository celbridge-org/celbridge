namespace Celbridge.WebHost;

/// <summary>
/// Whether the webview_* tools support a particular resource. When IsSupported
/// is false, Reason carries a human-readable explanation (e.g. document not
/// open, opened with the wrong editor, external-URL .webview, or the
/// contributing package opts out via DevToolsBlocked). Reason is null when
/// IsSupported is true.
/// </summary>
public sealed record WebViewToolSupport(bool IsSupported, string? Reason);

/// <summary>
/// Provides WebView-related services including URL classification.
/// </summary>
public interface IWebViewService
{
    /// <summary>
    /// Returns true if the URL is an external http/https URL suitable for navigating
    /// inside a .webview document. Local-scheme and relative-path URLs are rejected.
    /// </summary>
    bool IsExternalUrl(string url);

    /// <summary>
    /// Returns true if the WebViewDevTools feature flag is enabled in the
    /// user's .celbridge config. Callers that additionally need to block
    /// DevTools for a specific host (e.g. a bundled package that embeds
    /// sensitive material) should combine this with their own check.
    /// </summary>
    bool IsDevToolsFeatureEnabled();

    /// <summary>
    /// Returns true if the WebViewDevToolsEval feature flag is enabled. This is a
    /// separate flag from IsDevToolsFeatureEnabled because webview_eval is an
    /// arbitrary code execution primitive and is gated independently.
    /// </summary>
    bool IsDevToolsEvalFeatureEnabled();

    /// <summary>
    /// Determines whether the webview_* tools support the specified resource
    /// and, when not, returns a human-readable reason. The check inspects the
    /// open documents list and the package registry. When eligibility cannot
    /// be determined (e.g. no workspace is loaded) the result is treated as
    /// supported and the caller falls back to its own generic message.
    /// </summary>
    WebViewToolSupport GetWebViewToolSupport(ResourceKey resource);
}
