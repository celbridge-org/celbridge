using Celbridge.WebHost.Platform;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Chromium retries a download whose transfer broke off, and WebView2 announces each retry as a download of
/// its own. These tests pin how the attempt it gave up on is told from one still running, which is what
/// keeps a retry on the row it belongs to while two downloads of the same file keep a row each.
/// </summary>
[TestFixture]
public class WebView2DownloadHandlerTests
{
    [Test]
    public void AnAttemptInProgressWithTheReasonItBrokeOff_HasBeenAbandoned()
    {
        WebView2DownloadHandler.IsAbandoned(
            CoreWebView2DownloadState.InProgress,
            CoreWebView2DownloadInterruptReason.ServerContentLengthMismatch)
            .Should().BeTrue();
    }

    [Test]
    public void AnAttemptStillRunning_HasNotBeenAbandoned()
    {
        WebView2DownloadHandler.IsAbandoned(
            CoreWebView2DownloadState.InProgress,
            CoreWebView2DownloadInterruptReason.None)
            .Should().BeFalse();
    }

    [TestCase(CoreWebView2DownloadState.Interrupted)]
    [TestCase(CoreWebView2DownloadState.Completed)]
    public void AnAttemptThatHasEnded_HasNotBeenAbandoned(CoreWebView2DownloadState state)
    {
        // An attempt that ends reports it, and its download settles, so there is nothing for a retry to join.
        WebView2DownloadHandler.IsAbandoned(
            state,
            CoreWebView2DownloadInterruptReason.NetworkFailed)
            .Should().BeFalse();
    }
}
