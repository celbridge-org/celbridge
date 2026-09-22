using Celbridge.WebHost.Platform;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Chromium suggests a download's name after checking the operating system's Downloads folder, which is
/// not where the file goes. These tests pin that only the suffix Chromium adds for that folder is undone,
/// and that a name the server itself gave, suffix and all, is kept.
/// </summary>
[TestFixture]
public class WebView2SuggestedNameTests
{
    private const string SuggestedFolder = @"C:\Users\Someone\Downloads";

    private const string ReleaseUrl =
        "https://release-assets.githubusercontent.com/github-production-release-asset/123/abc-def?X-Amz-Signature=1";

    [Test]
    public void ASuffixChromiumAdded_IsUndone()
    {
        // The case that reads as a bug: a copy already in the OS Downloads folder gave Chromium's name a
        // counter that the project's own uniquifier then added to again.
        var fileName = WebView2SuggestedName.Resolve(
            Path.Combine(SuggestedFolder, "Celbridge.Application_1.0.0.0_x64 (1).msix"),
            "attachment; filename=Celbridge.Application_1.0.0.0_x64.msix",
            ReleaseUrl);

        fileName.Should().Be("Celbridge.Application_1.0.0.0_x64.msix");
    }

    [Test]
    public void ASuffixTheServerGave_IsKept()
    {
        var fileName = WebView2SuggestedName.Resolve(
            Path.Combine(SuggestedFolder, "report (1).pdf"),
            "attachment; filename=\"report (1).pdf\"",
            "https://example.com/files/report%20(1).pdf");

        fileName.Should().Be("report (1).pdf");
    }

    [Test]
    public void AnUnchangedSuggestion_IsKept()
    {
        var fileName = WebView2SuggestedName.Resolve(
            Path.Combine(SuggestedFolder, "notes.txt"),
            "attachment; filename=notes.txt",
            "https://example.com/notes.txt");

        fileName.Should().Be("notes.txt");
    }

    [Test]
    public void WithoutAHeader_TheUrlNameIsUsedToUndoTheSuffix()
    {
        var fileName = WebView2SuggestedName.Resolve(
            Path.Combine(SuggestedFolder, "data (2).csv"),
            contentDisposition: string.Empty,
            "https://example.com/exports/data.csv");

        fileName.Should().Be("data.csv");
    }

    [Test]
    public void AnEncodedHeaderName_IsPreferred()
    {
        var fileName = WebView2SuggestedName.Resolve(
            Path.Combine(SuggestedFolder, "résumé (1).pdf"),
            "attachment; filename=\"resume.pdf\"; filename*=UTF-8''r%C3%A9sum%C3%A9.pdf",
            ReleaseUrl);

        fileName.Should().Be("résumé.pdf");
    }

    [Test]
    public void ASuggestionChromiumChangedOtherwise_IsTrusted()
    {
        // Chromium sanitised the header's name, so its suggestion is not only a uniquified form of it.
        var fileName = WebView2SuggestedName.Resolve(
            Path.Combine(SuggestedFolder, "report_.pdf"),
            "attachment; filename=\"report?.pdf\"",
            ReleaseUrl);

        fileName.Should().Be("report_.pdf");
    }

    [Test]
    public void AHeaderNamingAPath_ContributesOnlyItsFileName()
    {
        var fileName = WebView2SuggestedName.Resolve(
            Path.Combine(SuggestedFolder, "payload (1).bin"),
            "attachment; filename=\"../../payload.bin\"",
            ReleaseUrl);

        fileName.Should().Be("payload.bin");
    }
}
