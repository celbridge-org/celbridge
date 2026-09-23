using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Celbridge.WebHost;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Celbridge.WebView.Services;

/// <summary>
/// Default navigation-policy helper. Intercepts WebView2 top-frame navigations,
/// invokes the supplied handler, and dispatches the handler's NavigationDecision.
/// </summary>
public sealed class WebViewNavigationPolicy : IWebViewNavigationPolicy
{
    // What an attached web view is subscribed with: the NavigationStarting handler, and the gate that puts
    // the same navigations to the handler before their requests are sent, on a head that needs one.
    private record Attachment(
        TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs> OnStarting,
        IDisposable Gate);

    private readonly ICommandService _commandService;
    private readonly IWebViewAdapter _webViewAdapter;
    private readonly ILogger<WebViewNavigationPolicy> _logger;

    private readonly Dictionary<CoreWebView2, Attachment> _attachments = new();

    public WebViewNavigationPolicy(
        ICommandService commandService,
        IWebViewAdapter webViewAdapter,
        ILogger<WebViewNavigationPolicy> logger)
    {
        _commandService = commandService;
        _webViewAdapter = webViewAdapter;
        _logger = logger;
    }

    public void Attach(CoreWebView2 webView, NavigationDestinationHandler handler)
    {
        TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs> onStarting = (sender, args) =>
        {
            HandleNavigationStarting(args, handler);
        };

        webView.NavigationStarting += onStarting;

        // Where the head has a gate, it takes the decision before the request is sent, so a destination the
        // handler refuses is never fetched. A navigation the gate lets through is asked about again at
        // NavigationStarting, which a handler that allowed it answers the same way. The Windows heads have
        // no gate and decide at NavigationStarting alone, by which time the request has gone out.
        var gate = _webViewAdapter.GateNavigations(webView, (destination, isUserInitiated) =>
            Decide(new NavigationRequest(destination, isUserInitiated), handler));

        _attachments[webView] = new Attachment(onStarting, gate);
    }

    public void Detach(CoreWebView2 webView)
    {
        if (_attachments.Remove(webView, out var attachment))
        {
            webView.NavigationStarting -= attachment.OnStarting;
            attachment.Gate.Dispose();
        }
    }

    private void HandleNavigationStarting(
        CoreWebView2NavigationStartingEventArgs args,
        NavigationDestinationHandler handler)
    {
        var uriText = args.Uri;
        if (string.IsNullOrEmpty(uriText))
        {
            return;
        }

        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var destination))
        {
            return;
        }

        var request = new NavigationRequest(destination, args.IsUserInitiated);
        if (!Decide(request, handler))
        {
            args.Cancel = true;
        }
    }

    /// <summary>
    /// Asks the handler about a navigation and returns whether it goes ahead. A navigation the handler
    /// refuses, or has not decided on by the time this returns, does not, and the side effect of the
    /// decision is dispatched once it is made.
    /// </summary>
    internal bool Decide(NavigationRequest request, NavigationDestinationHandler handler)
    {
        var destination = request.Destination;
        var decisionTask = handler(request);

        // Synchronous fast path. Most call sites - the .webview always-allow handler
        // and the HTML viewer's same-URL pinned-match check - complete synchronously.
        if (decisionTask.IsCompleted)
        {
            var decision = decisionTask.Result;
            if (decision == NavigationDecision.Allow)
            {
                return true;
            }

            _logger.LogDebug("Cancelled navigation to {Url}, decided {Decision}", destination, decision);

            DispatchSideEffect(decision, destination);
            return false;
        }

        // Async path. Refused at once so the WebView does not present the destination, then the handler is
        // awaited and any side effect dispatched. Refusing does not unsend a request the head has already
        // made: see IWebViewAdapter.GateNavigations for which heads fetch a refused destination anyway.
        _logger.LogDebug("Cancelled navigation to {Url} while the destination is decided", destination);

        _ = AwaitAndDispatchAsync(decisionTask, destination);
        return false;
    }

    private async Task AwaitAndDispatchAsync(Task<NavigationDecision> decisionTask, Uri destination)
    {
        try
        {
            var decision = await decisionTask;
            DispatchSideEffect(decision, destination);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Navigation destination handler threw");
        }
    }

    /// <summary>
    /// Translates a NavigationDecision into its non-cancellation side effect (or no-op).
    /// Allow and Cancel are no-ops here; the cancel itself is made by whoever asked for
    /// the decision. OpenInSystemBrowser routes through IOpenBrowserCommand.
    /// </summary>
    internal void DispatchSideEffect(NavigationDecision decision, Uri destination)
    {
        if (decision == NavigationDecision.OpenInSystemBrowser)
        {
            _commandService.Execute<IOpenBrowserCommand>(command =>
            {
                command.URL = destination.ToString();
            });
        }
    }
}
