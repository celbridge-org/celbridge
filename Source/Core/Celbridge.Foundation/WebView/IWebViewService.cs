namespace Celbridge.WebView;

/// <summary>
/// Classifies how a webview URL should be interpreted and resolved.
/// </summary>
public enum UrlType
{
    /// <summary>
    /// A web URL with http:// or https:// scheme.
    /// </summary>
    WebUrl,

    /// <summary>
    /// An absolute resource key with the local:// scheme
    /// (e.g. "local://Sites/index.html").
    /// </summary>
    LocalAbsolute,

    /// <summary>
    /// A local path, either relative to the .webapp document's location
    /// (e.g. "../index.html") or an absolute resource key (e.g. "Sites/index.html").
    /// Resolution tries relative first, then absolute.
    /// </summary>
    LocalPath,

    /// <summary>
    /// The URL could not be classified as a valid input.
    /// </summary>
    Invalid,
}

/// <summary>
/// Provides WebView-related services including URL classification
/// for .webapp documents.
/// </summary>
public interface IWebViewService
{
    /// <summary>
    /// Classifies a .webapp Home URL string to determine how it should
    /// be resolved. Returns WebUrl for http/https URLs, LocalAbsolute
    /// for local:// URLs, LocalPath for relative paths or resource keys
    /// ending in .html/.htm, or Invalid otherwise.
    /// </summary>
    UrlType ClassifyUrl(string url);

    /// <summary>
    /// Returns true if the URL requires the local file server to be ready
    /// before navigation can proceed.
    /// </summary>
    bool NeedsFileServer(string url);

    /// <summary>
    /// Strips the local:// prefix from an absolute local URL,
    /// returning the resource key path.
    /// </summary>
    string StripLocalScheme(string url);

    /// <summary>
    /// Returns true if the WebViewDevTools feature flag is enabled in the
    /// user's .celbridge config. Callers that additionally need to block
    /// DevTools for a specific host (e.g. a bundled package that embeds
    /// sensitive material) should combine this with their own check.
    /// </summary>
    bool IsDevToolsFeatureEnabled();
}
