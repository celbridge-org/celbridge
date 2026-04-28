using Celbridge.WebView.Services;

namespace Celbridge.WebView.Views;

/// <summary>
/// Per-instance options configuring a WebViewDocumentView. Constructed by the editor
/// factory and assigned to the view before LoadContent runs.
/// </summary>
internal sealed record WebViewDocumentOptions(
    WebViewDocumentRole Role,
    bool InterceptTopFrameNavigation);
