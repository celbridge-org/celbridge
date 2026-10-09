using System.Text.Json;

namespace Celbridge.WebHost;

/// <summary>
/// One notification a hosted page posted to its host over the native web message bus, carrying the view it came
/// from and the parameters the page sent with it.
/// </summary>
internal sealed record WebSurfaceMessage(IWebView View, JsonElement Parameters);

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
    /// Begins routing the view's messages. Attaching a view that is already attached has no effect.
    /// </summary>
    void Attach(IWebView view);

    /// <summary>
    /// Stops routing the view's messages. Safe to call for a view that was never attached.
    /// </summary>
    void Detach(IWebView view);
}
