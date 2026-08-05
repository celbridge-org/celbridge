using Celbridge.Commands;
using Celbridge.Logging;

namespace Celbridge.WebHost.Commands;

public class ClearBrowsingDataCommand : CommandBase, IClearBrowsingDataCommand
{
    private readonly ILogger<ClearBrowsingDataCommand> _logger;
    private readonly IWebViewAdapter _webViewAdapter;
    private readonly IWebViewFactory _webViewFactory;

    public ClearBrowsingDataCommand(
        ILogger<ClearBrowsingDataCommand> logger,
        IWebViewAdapter webViewAdapter,
        IWebViewFactory webViewFactory)
    {
        _logger = logger;
        _webViewAdapter = webViewAdapter;
        _webViewFactory = webViewFactory;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_webViewAdapter.SupportsLiveBrowsingDataClear)
        {
            return Result.Fail("Clearing browsing data is not supported on this platform");
        }

        WebView2? webView = null;
        try
        {
            if (_webViewAdapter.BrowsingDataClearRequiresInstance)
            {
                // Every WebView in the application shares one store, so a pooled instance reaches the same
                // store an open document would, and is available whether or not a project is loaded.
                webView = await _webViewFactory.AcquireAsync();
            }

            await _webViewAdapter.ClearBrowsingDataAsync(webView?.CoreWebView2);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear the browsing data");
            return Result.Fail("Failed to clear the browsing data").WithException(ex);
        }
        finally
        {
            if (webView is not null)
            {
                // The acquired instance was never parented, so there is no container to detach it from. The
                // pool replenishes itself in the background.
                _webViewAdapter.CloseWebView(webView, container: null);
            }
        }

        return Result.Ok();
    }
}
