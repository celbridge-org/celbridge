using Celbridge.Logging;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// A web view on an Uno Skia head. Where Uno leaves the WebView2 surface unimplemented, the view runs script in the
/// page instead.
/// </summary>
public class SkiaWebView : WebViewBase
{
    private readonly CoreWebView2 _coreWebView2;
    private readonly ILogger _logger;

    internal SkiaWebView(WebView2 control, SkiaWebViewPlatform platform, ILogger logger)
        : base(control, platform, logger)
    {
        _coreWebView2 = CoreWebView2!;
        _logger = logger;
    }

    // Only the developer tools setting applies, and it reaches the real WebView2 behind the Windows-under-Skia head.
    // No Skia head implements zoom control. Sites already recognise the default User-Agent of these heads.
    protected override void ApplyOptions(WebViewOptions options)
    {
        _coreWebView2.Settings.AreDevToolsEnabled = options.IsDevToolsEnabled;
    }

    protected override void LoadHtmlStringCore(string html, string baseUrl)
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

    // The Skia WebView2 does not implement AddScriptToExecuteOnDocumentCreatedAsync, so the script runs after
    // each navigation instead.
    protected override async Task<bool> InstallDocumentStartScriptAsync(string script)
    {
        await Task.CompletedTask;

        return false;
    }

    // The platform keeps the page's viewport in step with the control.
    protected override bool SetNativeViewportSize(double width, double height)
    {
        return false;
    }

    // WebKit faults where WebView2 silently returns the JSON literal "null". These faults are normalised to "null",
    // so common errors and undefined results read alike on every platform. evaluateJavaScript faults on a JS
    // exception or syntax error (WKError 4), and on an unsupported return type such as a Promise (WKError 5). Uno
    // surfaces an undefined result as an ArgumentNullException.
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

    // The Skia heads never raise WebView2's DownloadStarting, so this handler stays idle. On macOS, downloads come
    // from WebKit.
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
