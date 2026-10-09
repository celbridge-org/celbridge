using Celbridge.Logging;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// A web view on an Uno Skia head. Where Uno leaves the WebView2 surface unimplemented, it runs script in the page
/// instead. MacOSWebView extends it with the native WKWebView interop.
/// </summary>
internal class SkiaWebView : WebViewBase
{
    private readonly CoreWebView2 _coreWebView2;
    private readonly ILogger _logger;

    public SkiaWebView(WebView2 control, SkiaWebViewPlatform platform, ILogger logger)
        : base(control, platform, logger)
    {
        _coreWebView2 = CoreWebView2!;
        _logger = logger;
    }

    // The developer tools setting reaches the real WebView2 behind the Windows-under-Skia head. No Skia head
    // implements zoom control, and sites recognise the default User-Agent here.
    protected override void ApplyOptions(WebViewOptions options)
    {
        _coreWebView2.Settings.AreDevToolsEnabled = options.IsDevToolsEnabled;
    }

    public override void LoadHtmlString(string html, string baseUrl)
    {
        throw new NotSupportedException("Loading an HTML string with a base URL is not supported on this platform.");
    }

    // Uno's Stop reaches the native view, so it also cancels a load that has not yet produced a document.
    public override async Task StopAsync()
    {
        await Task.CompletedTask;

        _coreWebView2.Stop();
    }

    // Uno's Reload reaches the native view, so it also recovers a page whose script has hung. The HTTP cache is
    // left alone, since the Skia heads have no way to clear it for one page.
    protected override async Task ReloadPageAsync(bool clearCache)
    {
        await Task.CompletedTask;

        _coreWebView2.Reload();
    }

    public override async Task StartFindAsync(string term, FindOptions options)
    {
        await Task.CompletedTask;

        _logger.LogDebug("Whole-page find is not implemented on this Skia head");
    }

    public override void FindNext()
    {
    }

    public override void FindPrevious()
    {
    }

    public override void StopFind()
    {
    }

    // The Skia WebView2 does not implement AddScriptToExecuteOnDocumentCreatedAsync, so the script runs again
    // after each navigation instead.
    protected internal override async Task InstallDocumentStartScriptAsync(string script)
    {
        await Task.CompletedTask;
    }

    protected internal override async Task RerunDocumentStartScriptAsync(string script)
    {
        await _coreWebView2.ExecuteScriptAsync(script);
    }

    // The platform keeps the page's viewport in step with the control.
    protected override bool SetNativeViewportSize(double width, double height)
    {
        return false;
    }

    // WebKit's evaluateJavaScript faults on JS exceptions and syntax errors (WKError 4), on unsupported return
    // types such as Promises (WKError 5), and on an undefined result (surfaced by Uno as an
    // ArgumentNullException). WebView2 returns the JSON literal "null" silently in the equivalent cases. The
    // faults are normalised so common errors and undefined results read as None on Python callers across
    // platforms.
    protected override async Task<string> EvalScriptAsync(string expression)
    {
        try
        {
            return await EvaluateAsync(expression);
        }
        catch (ArgumentNullException)
        {
            return "null";
        }
        catch (Exception scriptEx) when (scriptEx.Message.Contains("WKErrorDomain", StringComparison.Ordinal))
        {
            return "null";
        }
    }

    /// <summary>
    /// Evaluates an expression and returns its value encoded as JSON.
    /// </summary>
    protected virtual async Task<string> EvaluateAsync(string expression)
    {
        var result = await _coreWebView2.ExecuteScriptAsync(expression);

        return result ?? "null";
    }

    protected override async Task<ScreenshotData> CaptureScreenshotCoreAsync(ScreenshotRequest request)
    {
        await Task.CompletedTask;

        throw new InvalidOperationException("Screenshots are not supported on this platform.");
    }

    // No Skia head raises WebView2's DownloadStarting, so the handler is never called here. MacOSWebView takes
    // downloads from WebKit instead.
    protected override IWebViewDownloadHandler CreateDownloadHandler()
    {
        return WebView2DownloadHandler.Attach(_coreWebView2);
    }

    // On the Windows Skia head, Uno passes on WebView2's Source, which changes as a navigation commits.
    protected override IDisposable ObserveNavigationCommits(NavigationCommitted onCommitted)
    {
        return new SourceChangedObserver(_coreWebView2, onCommitted);
    }
}
