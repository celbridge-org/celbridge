using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// One notification a hosted page posted to its host over the native web message bus, carrying the surface it
/// came from and the parameters the page sent with it.
/// </summary>
internal sealed record WebSurfaceMessage(CoreWebView2 Surface, string SurfaceName, JsonElement Parameters);

/// <summary>
/// Routes the notifications hosted pages post over the native web message bus to the handler registered for
/// each method. The bus carries the signals that cannot go through the JSON-RPC channel, because they come
/// from scripts the host injects into pages it did not author, where there is no client library on the other
/// end. Owning the bus in one place is what keeps a new page-to-host signal a handler registration rather than
/// another branch in whichever component happened to be listening.
/// </summary>
internal interface IWebSurfaceMessageDispatcher
{
    /// <summary>
    /// Registers the handler for a method, replacing any handler already registered for it. A message naming
    /// a method with no handler is ignored.
    /// </summary>
    void AddHandler(string method, Action<WebSurfaceMessage> handler);

    /// <summary>
    /// Begins routing the surface's messages under the given name. Attaching a surface that is already
    /// attached renames it and keeps the one subscription, so a pooled web view reacquired for another
    /// document reports under the document it now shows.
    /// </summary>
    void Attach(CoreWebView2 coreWebView, string surfaceName);

    /// <summary>
    /// Stops routing the surface's messages. Safe to call for a surface that was never attached.
    /// </summary>
    void Detach(CoreWebView2 coreWebView);
}
