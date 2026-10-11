using Celbridge.Settings;
using Celbridge.Tests.Helpers;
using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for how a view reports its page loads: how a navigation ends, and the probe that finds a page that
/// loaded empty. These views have no control, so the tests raise the platform's navigation events themselves.
/// </summary>
[TestFixture]
public class WebViewLoadReportTests
{
    private const string EmptyDocument =
        """{"url":"https://www.example.org/","readyState":"complete","htmlLength":39,"headChildCount":0,"bodyChildCount":0}""";

    private const string LoadedDocument =
        """{"url":"https://www.example.org/","readyState":"complete","htmlLength":-1,"headChildCount":9,"bodyChildCount":4}""";

    private static readonly WebNavigationCompletedEventArgs Succeeded = new(WebNavigationResult.Succeeded, string.Empty);
    private static readonly WebNavigationCompletedEventArgs Failed = new(WebNavigationResult.Failed, "Unknown");

    private RecordingLogger<ControlFreeWebView> _logger = null!;
    private ControlFreeWebView _view = null!;
    private int _loadedEmptyCount;
    private List<WebNavigationResult> _completions = null!;

    [SetUp]
    public async Task SetUp()
    {
        _logger = new RecordingLogger<ControlFreeWebView>();
        _view = new ControlFreeWebView(Substitute.For<IWebViewPlatform>(), _logger);
        await _view.ConfigureAsync(WebViewOptions.Default, Substitute.For<IFeatureFlags>());

        _loadedEmptyCount = 0;
        _view.LoadedEmpty += (_, _) => _loadedEmptyCount++;

        _completions = new List<WebNavigationResult>();
        _view.NavigationCompleted += (_, completion) => _completions.Add(completion.Result);
    }

    [TearDown]
    public void TearDown()
    {
        _view.Dispose();
    }

    [Test]
    public void APageThatLoadedEmpty_IsReportedAndLogged()
    {
        _view.Evaluate = _ => Task.FromResult(EmptyDocument);

        _view.OnNavigationCompleted(Succeeded);

        _loadedEmptyCount.Should().Be(1);
        _logger.EntriesAt(LogEntryLevel.Warning).Should().ContainSingle(entry => entry.Message!.Contains("empty document"));
    }

    [Test]
    public void APageWithContent_IsNotReported()
    {
        _view.Evaluate = _ => Task.FromResult(LoadedDocument);

        _view.OnNavigationCompleted(Succeeded);

        _loadedEmptyCount.Should().Be(0);
        _logger.EntriesAt(LogEntryLevel.Warning).Should().BeEmpty();
    }

    [Test]
    public void AFailedNavigation_IsNotProbed()
    {
        var evaluations = 0;
        _view.Evaluate = _ =>
        {
            evaluations++;
            return Task.FromResult(EmptyDocument);
        };

        _view.OnNavigationCompleted(Failed);

        evaluations.Should().Be(0);
        _loadedEmptyCount.Should().Be(0);
    }

    [Test]
    public void AFindingAboutAPageANavigationIsLeaving_IsDropped()
    {
        var pageReport = new TaskCompletionSource<string>();
        _view.Evaluate = _ => pageReport.Task;
        _view.OnNavigationCompleted(Succeeded);

        _view.OnNavigationStarting("https://www.example.org/next");
        pageReport.SetResult(EmptyDocument);

        _loadedEmptyCount.Should().Be(0);
    }

    // The owner's own navigation may report no start, as on the Skia heads while the view is detached.
    [Test]
    public void AFindingAboutAPageTheOwnerNavigatedAwayFrom_IsDropped()
    {
        var pageReport = new TaskCompletionSource<string>();
        _view.Evaluate = _ => pageReport.Task;
        _view.OnNavigationCompleted(Succeeded);

        _view.LoadHtmlString("<p>Next</p>", "https://www.example.org/");
        pageReport.SetResult(EmptyDocument);

        _loadedEmptyCount.Should().Be(0);
    }

    // WebKit announces the download first, and then ends the navigation it replaced with a failure.
    [Test]
    public void ANavigationThatBecameADownload_CompletesAsAborted()
    {
        _view.OnNavigationStarting("https://www.example.org/report.pdf");
        _view.Downloads.RaiseDownloadStarted();

        _view.OnNavigationCompleted(Failed);

        _completions.Should().Equal(WebNavigationResult.Aborted);
        _logger.EntriesAt(LogEntryLevel.Warning).Should().BeEmpty();
    }

    // Chromium ends the navigation before it announces the download, so the download is still recorded when the
    // owner navigates again.
    [Test]
    public void AFailureAfterAnEarlierDownload_StillFails()
    {
        _view.OnNavigationStarting("https://www.example.org/report.pdf");
        _view.OnNavigationCompleted(new WebNavigationCompletedEventArgs(WebNavigationResult.Aborted, "ConnectionAborted"));
        _view.Downloads.RaiseDownloadStarted();

        _view.LoadHtmlString("<p>Next</p>", "https://www.example.org/");
        _view.OnNavigationCompleted(Failed);

        _completions.Should().Equal(WebNavigationResult.Aborted, WebNavigationResult.Failed);
        _logger.EntriesAt(LogEntryLevel.Warning).Should().ContainSingle(entry => entry.Message!.StartsWith("Navigation failed"));
    }
}
