using Celbridge.Logging;

namespace Celbridge.WebHost;

internal sealed class WebSurfaceMessageDispatcher : IWebSurfaceMessageDispatcher
{
    private readonly ILogger<WebSurfaceMessageDispatcher> _logger;

    private readonly Dictionary<string, Action<WebSurfaceMessage>> _handlers = new(StringComparer.Ordinal);

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

    // The handler closes over the view, so the dispatcher keeps no record of the view. The view drops the handler
    // when the view closes.
    public void Observe(IWebView view)
    {
        view.WebMessageReceived += (_, message) => OnWebMessageReceived(view, message);
    }

    // A malformed web message must never crash the host.
    private void OnWebMessageReceived(IWebView view, string message)
    {
        try
        {
            if (!MentionsHandledMethod(message))
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
