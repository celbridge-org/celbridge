using Celbridge.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Services;

public class WebViewHostChannel : IHostChannel
{
    private readonly CoreWebView2 _coreWebView2;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger<WebViewHostChannel> _logger;
    private readonly IWebViewAdapter _webViewAdapter;
    private bool _isDetached;

    public WebViewHostChannel(CoreWebView2 coreWebView2)
    {
        _coreWebView2 = coreWebView2;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _logger = ServiceLocator.AcquireService<ILogger<WebViewHostChannel>>();
        _webViewAdapter = ServiceLocator.AcquireService<IWebViewAdapter>();
        _coreWebView2.WebMessageReceived += OnWebMessageReceived;
    }

    private void PostToWebView(string json)
    {
        try
        {
            _webViewAdapter.PostMessageToWeb(_coreWebView2, json);
        }
        catch (Exception ex)
        {
            // The WebView2 may have been disposed between the detach check and the call.
            _logger.LogWarning(ex, "Failed to post message to the WebView");
        }
    }

    public event EventHandler<string>? MessageReceived;
    public event EventHandler? Closed;

    public void PostMessage(string json)
    {
        if (_isDetached)
        {
            return;
        }

        if (_dispatcherQueue.HasThreadAccess)
        {
            PostToWebView(json);
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDetached)
                {
                    return;
                }

                PostToWebView(json);
            });
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string? message = null;

            // On the Windows heads the page posts via chrome.webview.postMessage(string), delivered
            // as a JS string.
            try
            {
                message = e.TryGetWebMessageAsString();
            }
            catch (Exception)
            {
                // On the macOS WKWebView head the page posts via window.webkit.messageHandlers, and the
                // message is delivered as JSON (an object), not a string, so TryGetWebMessageAsString
                // throws. Fall back to the raw JSON below, which is the JSON-RPC envelope itself.
            }

            if (string.IsNullOrEmpty(message))
            {
                message = e.WebMessageAsJson;
            }

            if (!string.IsNullOrEmpty(message))
            {
                MessageReceived?.Invoke(this, message);
            }
        }
        catch (Exception ex)
        {
            // This handler runs on the UI thread, so an escaping exception would be fatal. A
            // malformed web message must never crash the host.
            _logger.LogWarning(ex, "Failed to read a web message from the WebView");
        }
    }

    /// <summary>
    /// Detaches the message handler. Call when disposing the view.
    /// </summary>
    public void Detach()
    {
        if (_isDetached)
        {
            return;
        }

        _isDetached = true;
        _coreWebView2.WebMessageReceived -= OnWebMessageReceived;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
