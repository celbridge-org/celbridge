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

    [Test]
    public void ADownloadToTheProjectsDownloadsFolder_IsNotUserChosen()
    {
        // What an ordinary link download looks like, and what a Save As left on the dialog's own default
        // looks like: either way the file belongs in the project, where the dialog said it would go.
        WebView2DownloadHandler.IsUserChosenPath(@"C:\Projects\notes\downloads\report.pdf", @"C:\Projects\notes\downloads")
            .Should().BeFalse();
    }

    [TestCase(@"C:\Projects\notes\downloads\")]
    [TestCase(@"c:\projects\notes\downloads")]
    public void ADownloadToThatFolderSpeltDifferently_IsNotUserChosen(string downloadsFolderPath)
    {
        WebView2DownloadHandler.IsUserChosenPath(@"C:\Projects\notes\downloads\report.pdf", downloadsFolderPath)
            .Should().BeFalse();
    }

    [Test]
    public void ADownloadToAnyOtherFolder_IsUserChosen()
    {
        // Only a Save As dialog puts a download anywhere else, so the file goes where the user said.
        WebView2DownloadHandler.IsUserChosenPath(@"C:\Users\ada\Documents\report.pdf", @"C:\Projects\notes\downloads")
            .Should().BeTrue();
    }

    [Test]
    public void ADownloadToASubfolderOfTheProjectsDownloadsFolder_IsUserChosen()
    {
        // WebView2 downloads into the folder itself, so a subfolder was named in a dialog.
        WebView2DownloadHandler.IsUserChosenPath(@"C:\Projects\notes\downloads\invoices\report.pdf", @"C:\Projects\notes\downloads")
            .Should().BeTrue();
    }

    [Test]
    public void ADownloadWithNoFolderToCompareAgainst_IsNotUserChosen()
    {
        // No workspace, so nothing vouches for the path and the download is routed and reported instead.
        WebView2DownloadHandler.IsUserChosenPath(@"C:\Users\ada\Documents\report.pdf", string.Empty)
            .Should().BeFalse();
    }
}
