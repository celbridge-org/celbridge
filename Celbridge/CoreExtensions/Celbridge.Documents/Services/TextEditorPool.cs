using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Celbridge.Documents.Services;

public class TextEditorPool
{
    private readonly Queue<WebView2> _pool;
    private readonly int _maxPoolSize;

    public TextEditorPool(int poolSize)
    {
        _maxPoolSize = poolSize;
        _pool = new Queue<WebView2>();

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

    public async Task<WebView2> AcquireTextEditorWebView()
    {
        if (_pool.Count > 0)
        {
            var instance = _pool.Dequeue();
            return instance;
        }
        else
        {
            // Optionally create a new instance if the pool is empty            
            return await CreateTextEditorWebView();
        }
    }

    public void ReleaseTextEditorWebView(WebView2 webView)
    {
        // Todo: Reset Monaco editor state

        _pool.Enqueue(webView);
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

        // Set the default language
        // Todo: This is probably redundant? We now set the language when the content loads.
        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"window.language = 'text';");

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
