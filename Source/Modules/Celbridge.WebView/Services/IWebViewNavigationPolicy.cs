using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebView.Services;

/// <summary>
/// The action a navigation handler decided should be taken for an attempted top-frame
/// navigation. The policy helper itself is responsible for translating the decision
/// into a side effect (cancelling the WebView's navigation, opening the system browser).
/// </summary>
public enum NavigationDecision
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
/// An attempted top-frame navigation. IsUserInitiated is true when the user started it, as
/// by clicking a link, and false when the page started it by itself.
/// </summary>
public record NavigationRequest(Uri Destination, bool IsUserInitiated);

/// <summary>
/// Async callback that decides what should happen for a single attempted navigation. The
/// policy helper carries out the decision's side effect, such as opening the system
/// browser. A destination the handler deals with itself, such as a project file it opens
/// in Celbridge, is returned as Cancel.
/// </summary>
public delegate Task<NavigationDecision> NavigationDestinationHandler(NavigationRequest request);

/// <summary>
/// Wraps a head's navigation gating so the .webview view and the HTML viewer share a
/// single navigation-policy code path. Each role attaches with its own handler; the
/// helper translates the handler's decision into the matching side effect.
/// </summary>
public interface IWebViewNavigationPolicy
{
    /// <summary>
    /// Puts the given WebView's top-frame navigations to the supplied handler, wherever
    /// the head decides them, so a destination the handler refuses is kept from the page
    /// and, as far as the head allows, never fetched. Iframe navigations are always
    /// allowed.
    /// </summary>
    void Attach(CoreWebView2 webView, NavigationDestinationHandler handler);

    /// <summary>
    /// Removes the subscription created by Attach. Safe to call if Attach was never invoked.
    /// </summary>
    void Detach(CoreWebView2 webView);
}
