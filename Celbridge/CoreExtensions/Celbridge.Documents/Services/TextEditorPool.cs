using Microsoft.Web.WebView2.Core;

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
            instance.Visibility = Visibility.Visible;
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
        webView.Visibility = Visibility.Collapsed;
        _pool.Enqueue(webView);
    }

    private static async Task<WebView2> CreateTextEditorWebView()
    {
        var webView = new WebView2();

        await webView.EnsureCoreWebView2Async();
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "MonacoEditor",
            "monaco",
            CoreWebView2HostResourceAccessKind.Allow);

        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");
        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"window.language = 'text';");

        // Todo: Choose theme based on user settings
        var theme = "vs-dark";
        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"window.theme = '{theme}';");

        webView.CoreWebView2.Navigate("http://MonacoEditor/index.html");

        // Optionally hide the WebView2 instance
        webView.Visibility = Visibility.Collapsed;

        return webView;
    }
}
