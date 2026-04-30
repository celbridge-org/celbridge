using Celbridge.Tools;

namespace Celbridge.Tests.Tools;

[TestFixture]
public class WebViewScreenshotResolverTests
{
    private string _projectFolder = null!;

    [SetUp]
    public void SetUp()
    {
        _projectFolder = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(WebViewScreenshotResolverTests)}/{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder, recursive: true);
        }
    }

    [Test]
    public void Resolve_EmptySaveTo_UsesDefaultFolderWithCleanName()
    {
        var result = WebViewScreenshotResolver.Resolve(saveTo: "", format: "jpeg", _projectFolder);

        result.IsSuccess.Should().BeTrue();
        var key = result.Value.ToString();
        key.Should().StartWith("screenshots/screenshot-");
        key.Should().EndWith(".jpg");
        // No collision in a fresh folder, so the unsuffixed form should be used.
        key.Should().NotContain(".jpg-").And.MatchRegex(@"screenshots/screenshot-\d{8}-\d{6}\.jpg$");
    }

    [Test]
    public void Resolve_EmptySaveToWithPng_UsesPngExtension()
    {
        var result = WebViewScreenshotResolver.Resolve(saveTo: "", format: "png", _projectFolder);

        result.IsSuccess.Should().BeTrue();
        result.Value.ToString().Should().EndWith(".png");
    }

    [Test]
    public void Resolve_ExactResourceKeyWithMatchingExtension_PreservesKey()
    {
        var result = WebViewScreenshotResolver.Resolve(saveTo: "docs/output.png", format: "png", _projectFolder);

        result.IsSuccess.Should().BeTrue();
        result.Value.ToString().Should().Be("docs/output.png");
    }

    [Test]
    public void Resolve_JpgExtensionMatchesJpegFormat()
    {
        var result = WebViewScreenshotResolver.Resolve(saveTo: "docs/output.jpg", format: "jpeg", _projectFolder);

        result.IsSuccess.Should().BeTrue();
        result.Value.ToString().Should().Be("docs/output.jpg");
    }

    [Test]
    public void Resolve_JpegExtensionMatchesJpegFormat()
    {
        var result = WebViewScreenshotResolver.Resolve(saveTo: "docs/output.jpeg", format: "jpeg", _projectFolder);

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void Resolve_ExtensionFormatMismatch_Fails()
    {
        var result = WebViewScreenshotResolver.Resolve(saveTo: "docs/output.png", format: "jpeg", _projectFolder);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("does not match format");
    }

    [Test]
    public void Resolve_TxtExtension_FailsForBothFormats()
    {
        var resultJpeg = WebViewScreenshotResolver.Resolve(saveTo: "docs/output.txt", format: "jpeg", _projectFolder);
        var resultPng = WebViewScreenshotResolver.Resolve(saveTo: "docs/output.txt", format: "png", _projectFolder);

        resultJpeg.IsFailure.Should().BeTrue();
        resultPng.IsFailure.Should().BeTrue();
    }

    [Test]
    public void Resolve_TrailingSlashSaveTo_GeneratesAutoNameInThatFolder()
    {
        var result = WebViewScreenshotResolver.Resolve(saveTo: "docs/", format: "jpeg", _projectFolder);

        result.IsSuccess.Should().BeTrue();
        var key = result.Value.ToString();
        key.Should().StartWith("docs/screenshot-");
        key.Should().EndWith(".jpg");
    }

    [Test]
    public void Resolve_NoExtensionSaveTo_TreatedAsFolder()
    {
        // A path without a file extension is interpreted as a folder reference,
        // matching the agent's likely intent ("put a screenshot in this folder").
        var result = WebViewScreenshotResolver.Resolve(saveTo: "captures", format: "png", _projectFolder);

        result.IsSuccess.Should().BeTrue();
        var key = result.Value.ToString();
        key.Should().StartWith("captures/screenshot-");
        key.Should().EndWith(".png");
    }

    [Test]
    public void Resolve_CollisionWithExistingFile_AddsSequenceSuffix()
    {
        // Pre-create a file matching the timestamp pattern the saver will pick.
        // To do this deterministically without racing the wall clock, we let
        // the saver generate its first name, then re-run Resolve and confirm
        // the second call produces a -1 suffix.
        var first = WebViewScreenshotResolver.Resolve(saveTo: "screenshots/", format: "jpeg", _projectFolder);
        first.IsSuccess.Should().BeTrue();

        // Materialise the first name so the next probe collides.
        var firstResourceKey = first.Value.ToString();
        var firstAbsolute = Path.Combine(_projectFolder, firstResourceKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(firstAbsolute)!);
        File.WriteAllBytes(firstAbsolute, new byte[] { 0 });

        var second = WebViewScreenshotResolver.Resolve(saveTo: "screenshots/", format: "jpeg", _projectFolder);
        second.IsSuccess.Should().BeTrue();

        // If both calls landed in the same wall-clock second, the second name
        // should carry a -1 suffix. If they straddled a second boundary, the
        // names will differ in the timestamp and neither carries a suffix —
        // both outcomes are correct, so the assertion accepts either form.
        var secondKey = second.Value.ToString();
        secondKey.Should().NotBe(firstResourceKey);
        secondKey.Should().MatchRegex(@"screenshots/screenshot-\d{8}-\d{6}(-\d+)?\.jpg$");
    }

    [Test]
    public void Resolve_TraversalAttempt_RejectedByResourceKey()
    {
        // Defense-in-depth check: ResourceKey.IsValidKey rejects '..', so the
        // saveTo path cannot escape the project root via traversal.
        var result = WebViewScreenshotResolver.Resolve(saveTo: "../escape.png", format: "png", _projectFolder);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Invalid saveTo");
    }

    [Test]
    public void Resolve_BackslashInSaveTo_Rejected()
    {
        var result = WebViewScreenshotResolver.Resolve(saveTo: @"docs\output.png", format: "png", _projectFolder);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void Resolve_AbsolutePathSaveTo_Rejected()
    {
        var result = WebViewScreenshotResolver.Resolve(saveTo: "/etc/output.png", format: "png", _projectFolder);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void Resolve_UnsupportedFormat_Fails()
    {
        var result = WebViewScreenshotResolver.Resolve(saveTo: "", format: "webp", _projectFolder);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Unsupported screenshot format");
    }
}
