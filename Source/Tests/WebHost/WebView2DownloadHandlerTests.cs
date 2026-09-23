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

    // The comparison under test resolves both paths with the host's own path rules, so the paths here are
    // built with them too: Windows separators read as ordinary characters everywhere else, which leaves a
    // path in one unparsed lump and the comparison meaningless. The folder is mixed case on purpose, so
    // the case the comparison ignores really differs on a host whose temp path is all lower case.
    private static readonly string DownloadsFolder = Path.Combine(Path.GetTempPath(), "Notes", "Downloads");
    private static readonly string FileInDownloads = Path.Combine(DownloadsFolder, "report.pdf");
    private static readonly string FileElsewhere = Path.Combine(Path.GetTempPath(), "ada", "report.pdf");

    [Test]
    public void ADownloadToTheProjectsDownloadsFolder_IsNotUserChosen()
    {
        // What an ordinary link download looks like, and what a Save As left on the dialog's own default
        // looks like: either way the file belongs in the project, where the dialog said it would go.
        WebView2DownloadHandler.IsUserChosenPath(FileInDownloads, DownloadsFolder)
            .Should().BeFalse();
    }

    [Test]
    public void ADownloadToThatFolderWithATrailingSeparator_IsNotUserChosen()
    {
        WebView2DownloadHandler.IsUserChosenPath(FileInDownloads, DownloadsFolder + Path.DirectorySeparatorChar)
            .Should().BeFalse();
    }

    [Test]
    public void ADownloadToThatFolderInAnotherCase_IsNotUserChosen()
    {
        // Folders are compared without regard to case, as the file system the handler runs on treats them.
        WebView2DownloadHandler.IsUserChosenPath(FileInDownloads, DownloadsFolder.ToLowerInvariant())
            .Should().BeFalse();
    }

    [Test]
    public void ADownloadToAnyOtherFolder_IsUserChosen()
    {
        // Only a Save As dialog puts a download anywhere else, so the file goes where the user said.
        WebView2DownloadHandler.IsUserChosenPath(FileElsewhere, DownloadsFolder)
            .Should().BeTrue();
    }

    [Test]
    public void ADownloadToASubfolderOfTheProjectsDownloadsFolder_IsUserChosen()
    {
        // WebView2 downloads into the folder itself, so a subfolder was named in a dialog.
        WebView2DownloadHandler.IsUserChosenPath(Path.Combine(DownloadsFolder, "invoices", "report.pdf"), DownloadsFolder)
            .Should().BeTrue();
    }

    [Test]
    public void ADownloadWithNoFolderToCompareAgainst_IsNotUserChosen()
    {
        // No workspace, so nothing vouches for the path and the download is routed and reported instead.
        WebView2DownloadHandler.IsUserChosenPath(FileElsewhere, string.Empty)
            .Should().BeFalse();
    }
}
