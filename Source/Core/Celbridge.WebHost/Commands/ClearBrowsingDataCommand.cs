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

        IEditorWebView? webView = null;
        try
        {
            if (_webViewAdapter.BrowsingDataClearRequiresInstance)
            {
                // Every web view shares one store. A view of its own reaches it whether or not a project is
                // loaded.
                webView = await _webViewFactory.AcquireAsync(WebViewOptions.Default);
            }

            var coreWebView2 = (webView as WebViewBase)?.CoreWebView2;
            await _webViewAdapter.ClearBrowsingDataAsync(coreWebView2);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear the browsing data");
            return Result.Fail("Failed to clear the browsing data").WithException(ex);
        }
        finally
        {
            // The factory replaces the view in the background.
            webView?.Dispose();
        }

        return Result.Ok();
    }
}
