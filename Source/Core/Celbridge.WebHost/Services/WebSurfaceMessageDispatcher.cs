using Celbridge.Logging;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Celbridge.WebHost;

internal sealed class WebSurfaceMessageDispatcher : IWebSurfaceMessageDispatcher
{
    private readonly ILogger<WebSurfaceMessageDispatcher> _logger;

    private readonly Dictionary<string, Action<WebSurfaceMessage>> _handlers = new(StringComparer.Ordinal);

    // Each attached view's message handler. Used only on the UI thread, where views attach and detach and the
    // event raises.
    private readonly Dictionary<IWebView, TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs>>
        _messageHandlers = new();

    // The handled method names, held as an array because every message from every surface is tested against
    // all of them before it is worth parsing.
    private string[] _handledMethods = Array.Empty<string>();

    public WebSurfaceMessageDispatcher(ILogger<WebSurfaceMessageDispatcher> logger)
    {
        _logger = logger;
    }

    public void AddHandler(string method, Action<WebSurfaceMessage> handler)
    {
        _handlers[method] = handler;
        _handledMethods = _handlers.Keys.ToArray();
    }

    public void Attach(IWebView view)
    {
        if (_messageHandlers.ContainsKey(view)
            || GetCoreWebView2(view) is not CoreWebView2 coreWebView)
        {
            return;
        }

        // The handler closes over the view rather than reading the event's sender. On the packaged Windows head
        // the sender can be a different managed object for the same native view.
        TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs> messageHandler =
            (_, args) => OnWebMessageReceived(view, args);

        _messageHandlers[view] = messageHandler;
        coreWebView.WebMessageReceived += messageHandler;
    }

    public void Detach(IWebView view)
    {
        if (!_messageHandlers.Remove(view, out var messageHandler)
            || GetCoreWebView2(view) is not CoreWebView2 coreWebView)
        {
            return;
        }

        coreWebView.WebMessageReceived -= messageHandler;
    }

    private static CoreWebView2? GetCoreWebView2(IWebView view)
    {
        return (view as WebViewBase)?.CoreWebView2;
    }

    private void OnWebMessageReceived(IWebView view, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // This handler runs on the UI thread alongside the host channel reading the same event, so an
        // escaping exception would be fatal. A malformed web message must never crash the host.
        try
        {
            if (!_messageHandlers.ContainsKey(view))
            {
                return;
            }

            // Read as JSON rather than through TryGetWebMessageAsString, which throws on the macOS WKWebView
            // head where a message arrives as JSON rather than a string. That would cost a thrown exception
            // per message per surface, only to reach a discriminator.
            var message = e.WebMessageAsJson;
            if (string.IsNullOrEmpty(message)
                || !MentionsHandledMethod(message))
            {
                return;
            }

            var notification = WebMessageEnvelope.TryRead(message, _handledMethods);
            if (notification is null)
            {
                return;
            }

            if (!_handlers.TryGetValue(notification.Method, out var handler))
            {
                return;
            }

            var surfaceMessage = new WebSurfaceMessage(view, notification.Parameters);

            handler.Invoke(surfaceMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch a message from a hosted web surface");
        }
    }

    // Every message a surface sends its host arrives on the same event, including editor content, so this
    // only pre-filters: matching decides whether the message is worth parsing, never what is done with it.
    private bool MentionsHandledMethod(string message)
    {
        foreach (var method in _handledMethods)
        {
            if (message.Contains(method, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
