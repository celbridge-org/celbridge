using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebView.Services;

/// <summary>
/// The action a navigation handler decided should be taken for an attempted top-frame
/// navigation. The policy helper itself is responsible for translating the decision
/// into a side effect (cancelling the WebView's navigation, opening the system browser).
/// </summary>
internal enum NavigationDecision
{
    /// <summary>
    /// Pass the navigation through to the WebView unchanged.
    /// </summary>
    Allow,

    /// <summary>
    /// Cancel the WebView's navigation and open the destination in the user's default
    /// system browser via IOpenBrowserCommand.
    /// </summary>
    OpenInSystemBrowser,

    /// <summary>
    /// Cancel the WebView's navigation with no further side effect.
    /// </summary>
    Cancel,
}

/// <summary>
/// Async callback that decides what should happen for a single attempted navigation.
/// Implementations are expected to be pure UI - showing a dialog, returning a choice -
/// without dispatching the resulting action themselves; the policy helper handles dispatch.
/// </summary>
internal delegate Task<NavigationDecision> NavigationDestinationHandler(Uri destination);

/// <summary>
/// Wraps WebView2 NavigationStarting interception so the .webview view and the HTML
/// viewer share a single navigation-policy code path. Each role attaches with its own
/// handler; the helper translates the handler's decision into the matching side effect.
/// </summary>
internal interface IWebViewNavigationPolicy
{
    /// <summary>
    /// Subscribes the supplied handler to NavigationStarting on the given WebView. The
    /// handler is consulted for every top-frame navigation; iframe navigations are
    /// always allowed.
    /// </summary>
    void Attach(CoreWebView2 webView, NavigationDestinationHandler handler);

    /// <summary>
    /// Removes the subscription created by Attach. Safe to call if Attach was never invoked.
    /// </summary>
    void Detach(CoreWebView2 webView);
}
