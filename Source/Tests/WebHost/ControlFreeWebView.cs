using Celbridge.Logging;
using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// A view built on WebViewBase with no control, for testing the shared view itself. The page answers each
/// evaluation through Evaluate, and the download handler can report a download.
/// </summary>
internal sealed class ControlFreeWebView : WebViewBase
{
    public ControlFreeWebView(IWebViewPlatform platform, ILogger logger)
        : base(null, platform, logger)
    {
    }

    public Func<string, Task<string>> Evaluate { get; set; } = _ => Task.FromResult("null");

    public FakeDownloadHandler Downloads { get; } = new();

    public override Task StopAsync() => Task.CompletedTask;

    public override Task StartFindAsync(string term, FindOptions options) => Task.CompletedTask;

    public override void FindNext()
    {
    }

    public override void FindPrevious()
    {
    }

    public override void StopFind()
    {
    }

    protected override void ApplyOptions(WebViewOptions options)
    {
    }

    protected override void LoadHtmlStringCore(string html, string baseUrl)
    {
    }

    protected override Task<bool> InstallDocumentStartScriptAsync(string script) => Task.FromResult(true);

    protected override bool SetNativeViewportSize(double width, double height) => true;

    protected override Task<string> EvalScriptAsync(string expression) => Evaluate(expression);

    protected override Task ReloadPageAsync(bool clearCache) => Task.CompletedTask;

    protected override Task<ScreenshotData> CaptureScreenshotCoreAsync(ScreenshotRequest request) =>
        throw new NotSupportedException();

    protected override IWebViewDownloadHandler CreateDownloadHandler() => Downloads;

    protected override IDisposable ObserveNavigationCommits(NavigationCommitted onCommitted) =>
        throw new NotSupportedException();

    /// <summary>
    /// A download handler that reports a download when told to.
    /// </summary>
    public sealed class FakeDownloadHandler : IWebViewDownloadHandler
    {
        public event EventHandler? DownloadStarted;

        public void RaiseDownloadStarted()
        {
            DownloadStarted?.Invoke(this, EventArgs.Empty);
        }

        public void Detach()
        {
        }
    }
}
