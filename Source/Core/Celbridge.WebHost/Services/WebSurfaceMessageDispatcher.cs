using Celbridge.Logging;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Celbridge.WebHost;

internal sealed class WebSurfaceMessageDispatcher : IWebSurfaceMessageDispatcher
{
    private readonly ILogger<WebSurfaceMessageDispatcher> _logger;

    private readonly Dictionary<string, Action<WebSurfaceMessage>> _handlers = new(StringComparer.Ordinal);

    // Attached surfaces, keyed by the CoreWebView2 the subscription was made on. Accessed only on the UI
    // thread: Attach and Detach run from view lifecycle, and the event they subscribe raises there.
    private readonly Dictionary<CoreWebView2, AttachedSurface> _surfaces = new();

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

    public void Attach(CoreWebView2 coreWebView, string surfaceName)
    {
        if (_surfaces.TryGetValue(coreWebView, out var attachedSurface))
        {
            _surfaces[coreWebView] = attachedSurface with { SurfaceName = surfaceName };
            return;
        }

        // The CoreWebView2 handed to the event is a different managed projection of the same native object
        // than the one attached here, so it cannot be used to find the surface. Each subscription closes over
        // the key it was attached under.
        TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs> messageHandler =
            (_, args) => OnWebMessageReceived(coreWebView, args);

        _surfaces[coreWebView] = new AttachedSurface(surfaceName, messageHandler);
        coreWebView.WebMessageReceived += messageHandler;
    }

    public void Detach(CoreWebView2 coreWebView)
    {
        if (!_surfaces.Remove(coreWebView, out var attachedSurface))
        {
            return;
        }

        coreWebView.WebMessageReceived -= attachedSurface.MessageHandler;
    }

    private void OnWebMessageReceived(CoreWebView2 coreWebView, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // This handler runs on the UI thread alongside the host channel reading the same event, so an
        // escaping exception would be fatal. A malformed web message must never crash the host.
        try
        {
            if (!_surfaces.TryGetValue(coreWebView, out var attachedSurface))
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

            var surfaceMessage = new WebSurfaceMessage(
                coreWebView,
                attachedSurface.SurfaceName,
                notification.Parameters);

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

    private sealed record AttachedSurface(
        string SurfaceName,
        TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs> MessageHandler);
}
