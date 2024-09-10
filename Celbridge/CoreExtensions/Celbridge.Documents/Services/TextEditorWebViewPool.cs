using CommunityToolkit.Diagnostics;
using Microsoft.Web.WebView2.Core;
using System.Collections.Concurrent;
using Windows.Foundation;

namespace Celbridge.Documents.Services;

public class TextEditorWebViewPool
{
    private readonly ConcurrentQueue<WebView2> _pool;
    private readonly int _maxPoolSize;

    public TextEditorWebViewPool(int poolSize)
    {
        _maxPoolSize = poolSize;
        _pool = new ConcurrentQueue<WebView2>();

        InitializePool();
    }

    private async void InitializePool()
    {
        for (int i = 0; i < _maxPoolSize; i++)
        {
            var webView = await CreateTextEditorWebView();

            _pool.Enqueue(webView);
        }
    }

    public async Task<WebView2> AcquireTextEditorWebView(string language)
    {
        WebView2? webView;
        if (!_pool.TryDequeue(out webView))
        {
            // Create a new instance if the pool is empty            
            webView = await CreateTextEditorWebView();
        }
        Guard.IsNotNull(webView);

        // Set the Monaco editor language
        var script = $"monaco.editor.setModelLanguage(window.editor.getModel(), '{language}');";
        await webView.CoreWebView2.ExecuteScriptAsync(script);

        return webView;
    }

    public async void ReleaseTextEditorWebView(WebView2 webView)
    {
        // Todo: This isn't really pooling as we're allowing the existing WebView to go out of scope and
        // instantiating a completely new WebView. This does ensure that the web view & Monaco editor
        // start in a pristine state, but we might want to try reusing the existing instance at some point.

        webView.CoreWebView2.Navigate("about:blank");

        var newWebView = await CreateTextEditorWebView();
        _pool.Enqueue(newWebView);
    }

    private static async Task<WebView2> CreateTextEditorWebView()
    {
        var webView = new WebView2();

        // This fixes a visual bug where the WebView2 control would show a white background briefly when
        // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        webView.DefaultBackgroundColor = Colors.Transparent;

        await webView.EnsureCoreWebView2Async();
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "MonacoEditor",
            "monaco",
            CoreWebView2HostResourceAccessKind.Allow);

        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

        // Todo: Choose theme based on user settings
        var theme = "vs-dark";
        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"window.theme = '{theme}';");

        webView.CoreWebView2.Navigate("http://MonacoEditor/index.html");

        bool isEditorReady = false;
        TypedEventHandler<WebView2, CoreWebView2WebMessageReceivedEventArgs> onWebMessageReceived = (sender, e) =>
        {
            var message = e.TryGetWebMessageAsString();

            if (message == "editor_ready")
            {
                isEditorReady = true;
                return;
            }

            throw new InvalidOperationException($"Expected 'editor_ready' message, but received: {message}");
        };

        webView.WebMessageReceived += onWebMessageReceived;

        while (!isEditorReady)
        {
            await Task.Delay(50);
        }

        webView.WebMessageReceived -= onWebMessageReceived;

        return webView;
    }
}
