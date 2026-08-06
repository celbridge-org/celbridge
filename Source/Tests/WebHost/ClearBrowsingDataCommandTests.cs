using Celbridge.Tests.Helpers;
using Celbridge.WebHost;
using Celbridge.WebHost.Commands;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for the clear browsing data command. The clear itself happens inside the platform adapter,
/// so the tests assert which store the command reaches for rather than the state of any WebView.
/// </summary>
[TestFixture]
public class ClearBrowsingDataCommandTests
{
    private IWebViewAdapter _webViewAdapter = null!;
    private IWebViewFactory _webViewFactory = null!;

    [SetUp]
    public void Setup()
    {
        _webViewAdapter = Substitute.For<IWebViewAdapter>();
        _webViewFactory = Substitute.For<IWebViewFactory>();

        _webViewAdapter.SupportsLiveBrowsingDataClear.Returns(true);
    }

    [Test]
    public async Task PlatformCannotClear_FailsWithoutTouchingTheAdapter()
    {
        _webViewAdapter.SupportsLiveBrowsingDataClear.Returns(false);

        var result = await CreateCommand().ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        await _webViewAdapter.DidNotReceive().ClearBrowsingDataAsync(Arg.Any<CoreWebView2?>());
    }

    [Test]
    public async Task StoreReachableWithoutAnInstance_ClearsWithoutAcquiringAWebView()
    {
        // The macOS shape: the default WKWebsiteDataStore is process-wide, so taking an instance from the
        // pool would cost a WebView creation for nothing.
        _webViewAdapter.BrowsingDataClearRequiresInstance.Returns(false);

        var result = await CreateCommand().ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _webViewFactory.DidNotReceive().AcquireAsync();
        await _webViewAdapter.Received(1).ClearBrowsingDataAsync(null);
    }

    [Test]
    public async Task AdapterThrows_ReportsAFailure()
    {
        _webViewAdapter.BrowsingDataClearRequiresInstance.Returns(false);
        _webViewAdapter.ClearBrowsingDataAsync(Arg.Any<CoreWebView2?>())
            .Returns(Task.FromException(new InvalidOperationException("The clear did not complete")));

        var result = await CreateCommand().ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }

    private ClearBrowsingDataCommand CreateCommand()
    {
        return new ClearBrowsingDataCommand(
            new NullLogger<ClearBrowsingDataCommand>(),
            _webViewAdapter,
            _webViewFactory);
    }
}
