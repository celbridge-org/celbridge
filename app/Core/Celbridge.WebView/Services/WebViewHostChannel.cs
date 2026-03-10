using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebView.Services;

/// <summary>
/// Implementation of IHostChannel that wraps CoreWebView2.
/// </summary>
public class WebViewHostChannel : IHostChannel
{
    private readonly CoreWebView2 _coreWebView2;
    private readonly DispatcherQueue _dispatcherQueue;

    public WebViewHostChannel(CoreWebView2 coreWebView2)
    {
        _coreWebView2 = coreWebView2;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _coreWebView2.WebMessageReceived += OnWebMessageReceived;
    }

    public event EventHandler<string>? MessageReceived;

    public void PostMessage(string json)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            _coreWebView2.PostWebMessageAsString(json);
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => _coreWebView2.PostWebMessageAsString(json));
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = e.TryGetWebMessageAsString();
        if (!string.IsNullOrEmpty(message))
        {
            MessageReceived?.Invoke(this, message);
        }
    }

    /// <summary>
    /// Detaches the message handler. Call when disposing the view.
    /// </summary>
    public void Detach()
    {
        _coreWebView2.WebMessageReceived -= OnWebMessageReceived;
    }
}
