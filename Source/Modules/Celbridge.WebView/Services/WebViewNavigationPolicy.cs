using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Logging;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Celbridge.WebView.Services;

/// <summary>
/// Default navigation-policy helper. Intercepts WebView2 top-frame navigations,
/// invokes the supplied handler, and dispatches the handler's NavigationDecision.
/// </summary>
public sealed class WebViewNavigationPolicy : IWebViewNavigationPolicy
{
    private readonly ICommandService _commandService;
    private readonly ILogger<WebViewNavigationPolicy> _logger;

    private readonly Dictionary<CoreWebView2, TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs>> _attachedHandlers = new();

    public WebViewNavigationPolicy(
        ICommandService commandService,
        ILogger<WebViewNavigationPolicy> logger)
    {
        _commandService = commandService;
        _logger = logger;
    }

    public void Attach(CoreWebView2 webView, NavigationDestinationHandler handler)
    {
        TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs> onStarting = (sender, args) =>
        {
            HandleNavigationStarting(args, handler);
        };

        webView.NavigationStarting += onStarting;
        _attachedHandlers[webView] = onStarting;
    }

    public void Detach(CoreWebView2 webView)
    {
        if (_attachedHandlers.TryGetValue(webView, out var onStarting))
        {
            webView.NavigationStarting -= onStarting;
            _attachedHandlers.Remove(webView);
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

        var decisionTask = handler(destination);

        // Synchronous fast path. Most call sites - the .webview always-allow handler
        // and the HTML viewer's same-URL pinned-match check - complete synchronously.
        if (decisionTask.IsCompleted)
        {
            var decision = decisionTask.Result;
            if (decision != NavigationDecision.Allow)
            {
                args.Cancel = true;
                DispatchSideEffect(decision, destination);
            }
            return;
        }

        // Async path. Cancel synchronously so the WebView never starts loading the
        // destination, then await the handler and dispatch any side effect.
        args.Cancel = true;
        _ = AwaitAndDispatchAsync(decisionTask, destination);
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
    /// Allow and Cancel are no-ops here; the cancel itself is set on the WebView2 args
    /// at the call site. OpenInSystemBrowser routes through IOpenBrowserCommand.
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
