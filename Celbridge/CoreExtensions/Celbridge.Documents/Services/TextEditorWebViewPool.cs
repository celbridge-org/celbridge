using Celbridge.UserInterface;
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

#if WINDOWS
        InitializePool();
#endif
    }

    private async void InitializePool()
    {
        for (int i = 0; i < _maxPoolSize; i++)
        {
            var webView = await CreateTextEditorWebView();

            _pool.Enqueue(webView);
        }
    }

    public async Task<WebView2> AcquireInstance()
    {
        WebView2? webView;
        if (!_pool.TryDequeue(out webView))
        {
            // Create a new instance if the pool is empty            
            webView = await CreateTextEditorWebView();
        }
        Guard.IsNotNull(webView);

        return webView;
    }

    public async void ReleaseInstance(WebView2 webView)
    {
        // Todo: This isn't really pooling as we're allowing the existing WebView to go out of scope and
        // then instantiating a completely new WebView. This ensures that the web view & Monaco editor start in a
        // pristine state, but we might want to try reusing the existing instance to improve performance and memory usage.

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

        // Set Monaco color theme to match the user interface theme
        var serviceProvider = ServiceLocator.ServiceProvider;
        var userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();
        var theme = userInterfaceService.UserInterfaceTheme;
        var vsTheme = theme == UserInterfaceTheme.Light ? "vs-light" : "vs-dark";
        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"window.theme = '{vsTheme}';");

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
