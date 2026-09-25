using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Celbridge.WebHost;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebView.Services;

/// <summary>
/// Default navigation-policy helper. Puts a page's top-frame navigations to the supplied handler through
/// the head's own gate, and dispatches the handler's NavigationDecision.
/// </summary>
public sealed class WebViewNavigationPolicy : IWebViewNavigationPolicy
{
    private readonly ICommandService _commandService;
    private readonly IWebViewAdapter _webViewAdapter;
    private readonly ILogger<WebViewNavigationPolicy> _logger;

    // The gate each attached web view's page is held by, until the surface detaches it.
    private readonly Dictionary<CoreWebView2, IDisposable> _gates = new();

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
        // A web view attached a second time would otherwise keep the first gate as well, and answer each of
        // its navigations twice.
        Detach(webView);

        // The head decides where it puts a navigation to the gate, and how it keeps a refused one from
        // being fetched. A head that can ask in more than one place asks in each, so a handler must answer
        // the same way every time.
        _gates[webView] = _webViewAdapter.GateNavigations(webView, (destination, isUserInitiated) =>
            Decide(new NavigationRequest(destination, isUserInitiated), handler));
    }

    public void Detach(CoreWebView2 webView)
    {
        if (_gates.Remove(webView, out var gate))
        {
            gate.Dispose();
        }
    }

    /// <summary>
    /// Puts a navigation to the handler and returns whether it may go ahead. True only when the handler
    /// allows it straight away. A refusal returns false, and so does a decision still being made.
    /// Whatever the decision turns out to be, its side effect is dispatched once it arrives.
    /// </summary>
    internal bool Decide(NavigationRequest request, NavigationDestinationHandler handler)
    {
        var destination = request.Destination;
        var decisionTask = handler(request);

        // Synchronous fast path, which the HTML viewer's same-URL pinned-match check takes.
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
        // awaited and any side effect dispatched. Refusing while the user is asked is what keeps the
        // destination unfetched: a head sends the request the moment a navigation is let through.
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
