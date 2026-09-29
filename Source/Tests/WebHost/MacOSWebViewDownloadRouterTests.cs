using Celbridge.WebHost.Platform;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// On macOS a navigation's response is downloaded rather than displayed by the rule a browser applies,
/// since WebKit left to itself drops what it cannot display. These tests pin that rule and how the
/// Content-Disposition header is read for it.
/// </summary>
[TestFixture]
public class MacOSWebViewDownloadRouterTests
{
    [Test]
    public void AResponseWebKitCanDisplay_IsDisplayed()
    {
        var policy = MacOSWebViewDownloadRouter.DecideResponsePolicy(canShowMimeType: true, contentDisposition: string.Empty);

        policy.Should().Be(MacNavigationResponsePolicy.Allow);
    }

    [Test]
    public void AResponseWebKitCannotDisplay_IsDownloaded()
    {
        var policy = MacOSWebViewDownloadRouter.DecideResponsePolicy(canShowMimeType: false, contentDisposition: string.Empty);

        policy.Should().Be(MacNavigationResponsePolicy.Download);
    }

    [Test]
    public void AnAttachment_IsDownloadedEvenWhereItCouldBeDisplayed()
    {
        var policy = MacOSWebViewDownloadRouter.DecideResponsePolicy(
            canShowMimeType: true,
            contentDisposition: "attachment; filename=notes.txt");

        policy.Should().Be(MacNavigationResponsePolicy.Download);
    }

    [Test]
    public void AnInlineResponse_IsDisplayed()
    {
        var policy = MacOSWebViewDownloadRouter.DecideResponsePolicy(
            canShowMimeType: true,
            contentDisposition: "inline; filename=report.pdf");

        policy.Should().Be(MacNavigationResponsePolicy.Allow);
    }

    [TestCase("attachment")]
    [TestCase("Attachment; filename=\"a.zip\"")]
    [TestCase("  attachment  ;filename=a.zip")]
    public void TheDispositionType_IsReadWithoutRegardToCaseOrSpacing(string contentDisposition)
    {
        MacOSWebViewDownloadRouter.IsAttachment(contentDisposition).Should().BeTrue();
    }

    [TestCase("")]
    [TestCase("inline")]
    [TestCase("attachments")]
    [TestCase("inline; filename=attachment.zip")]
    public void OnlyAnAttachmentType_MarksAnAttachment(string contentDisposition)
    {
        MacOSWebViewDownloadRouter.IsAttachment(contentDisposition).Should().BeFalse();
    }

    // WebKit saves a server's error page as the downloaded file, so these statuses fail the download.
    [TestCase(400)]
    [TestCase(403)]
    [TestCase(404)]
    [TestCase(500)]
    [TestCase(503)]
    public void AnErrorStatus_FailsTheDownload(int statusCode)
    {
        MacOSWebViewDownloadRouter.IsErrorStatus(statusCode).Should().BeTrue();
    }

    // A status of 0 is a response that did not come over HTTP, such as a blob or data URL.
    [TestCase(0)]
    [TestCase(200)]
    [TestCase(206)]
    [TestCase(304)]
    public void ASuccessOrNonHttpStatus_LetsTheDownloadProceed(int statusCode)
    {
        MacOSWebViewDownloadRouter.IsErrorStatus(statusCode).Should().BeFalse();
    }
}
